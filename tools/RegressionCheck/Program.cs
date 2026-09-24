using System.Diagnostics;
using System.Reflection;
using MawaqitAdhan.Core;
using MawaqitAdhan.Ui;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        // Tests cannot modify the user's settings, downloaded files or cached timetable.
        Environment.SetEnvironmentVariable("MAWAQIT_ADHAN_DATA_DIR",
            Path.Combine(Path.GetTempPath(), "MawaqitAdhan-tests", Guid.NewGuid().ToString("N")));
        try
        {
            AppPaths.EnsureCreated();
            DataChecks();
            UiChecks();
            SynchronizationContext.SetSynchronizationContext(null);
            PlaybackChecks().GetAwaiter().GetResult();
            if (args.Contains("--audio")) AudioCheck();
            if (args.Contains("--online")) DownloadCheck().GetAwaiter().GetResult();
            Console.WriteLine($"ALL {_checks} CHECKS PASSED");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void DataChecks()
    {
        File.WriteAllText(AppPaths.SettingsFile, "{\"MosqueSlug\":\"omar-witten\",\"LastFetchUtc\":\"2026-09-20T12:30:00Z\",\"AdhanEnabled\":[false],\"Volume\":200,\"FiredKeys\":[\"2026-09-20|Fajr\",null]}");
        var s = AppSettings.Load();
        Check(s.HasMosque && s.LastFetchUtc.Value.ToUniversalTime().Hour == 12, "loads original ISO-date settings");
        Check(s.AdhanEnabled.Length == 5 && !s.AdhanEnabled[0] && s.AdhanEnabled[4] && s.Volume == 100, "normalises damaged settings");
        Check(s.DuckOtherAudio && s.AdhanVoice == "adhan-madina", "missing settings retain defaults");
        Check(s.FiredKeys.Single() == "omar-witten|2026-09-20|Fajr", "migrates existing replay markers");
        s.Save(); s.Volume = 17; s.Save();
        Check(AppSettings.Load().Volume == 17, "settings atomically replace and round-trip");
        File.WriteAllText(AppPaths.SettingsFile, "{broken");
        Check(!AppSettings.Load().HasMosque, "corrupt settings recover safely");
        File.WriteAllText(AppPaths.SettingsFile, "{\"AdhanEnabled\":null,\"FiredKeys\":null,\"AdhanVoice\":null}");
        Check(AppSettings.Load().AdhanEnabled.All(x => x) && AppSettings.Load().AdhanVoice != null, "null settings recover defaults");
        Check(MawaqitClient.ExtractSlug("https://mawaqit.net.evil.test/en/a") == null &&
            MawaqitClient.ExtractSlug("../settings") == null && MawaqitClient.ExtractSlug("https://other.test/a") == null, "rejects invalid hosts and path slugs");
        Check(MawaqitClient.ExtractSlug("mawaqit.net/de/omar-witten/?a=b") == "omar-witten", "accepts mawaqit links without scheme");
        Check(Timetable.ParseClock("12:30:garbage") == null, "rejects malformed times");
        var json = "{\"name\":\"Quoted } brace\",\"timezone\":\"Europe/Berlin\",\"times\":[\"05:00\",\"12:00\",\"15:00\",\"18:00\",\"20:00\"],\"shuruq\":\"06:00\"}";
        Check(MawaqitClient.ExtractConfData("let confData = " + json + "; other()") == json, "extracts JSON with braces inside strings");
        var table = MawaqitClient.ParseConfData(json, "fixture");
        Check(PrayerSchedule.Day(table, table.MosqueToday()).Count == 6, "today-only fallback works");
        Check(table.TimesFor(table.MosqueToday().AddDays(1)) == null, "today-only times never repeat tomorrow");
        Check(table.TimesFor(table.MosqueToday().AddYears(1)) == null, "old calendars are not reused in a new year");
        table.SaveCache(); table.SaveCache();
        var cached = Timetable.LoadCache("fixture");
        Check(cached != null && cached.FetchedUtc.ToUniversalTime().Date == DateTime.UtcNow.Date &&
            PrayerSchedule.Day(cached, cached.MosqueToday()).Count == 6, "cache and dates round-trip");
        var berlin = TimeZones.Find("Europe/Berlin");
        Check(PrayerSchedule.ToInstant(new DateTime(2026, 3, 29, 2, 30, 0), berlin).Hour == 3, "one-hour DST gap preserves minutes");
        Check(PrayerSchedule.ToInstant(new DateTime(2026, 10, 25, 2, 30, 0), berlin).Offset.TotalHours == 2, "DST overlap chooses first occurrence");
        var halfHour = PrayerSchedule.ToInstant(new DateTime(2026, 10, 4, 2, 15, 0), TimeZones.Find("Australia/Lord_Howe"));
        Check(halfHour.Hour == 2 && halfHour.Minute == 45 && halfHour.Offset.TotalHours == 11, "half-hour DST gap preserves minutes");
        Check(TimeZones.Find("Asia/Jakarta").GetUtcOffset(DateTime.UtcNow).TotalHours == 7 &&
            TimeZones.Find("America/Toronto").GetUtcOffset(new DateTime(2026, 7, 1)).TotalHours == -4, "remote IANA zones map to Windows rules");
        Check(TimeZones.Find("Asia/Ho_Chi_Minh").Id == TimeZones.Find("Asia/Saigon").Id &&
            TimeZones.Find("Asia/Kolkata").Id == "India Standard Time", "modern IANA aliases are resolved");
        try { TimeZones.Find("Invalid/Zone"); Check(false, "unknown zone"); }
        catch (TimeZoneNotFoundException) { Check(true, "unknown zones fail instead of firing at local time"); }
        var a = new PrayerEvent { MosqueSlug = "one", MosqueDate = DateTime.Today, Prayer = Prayer.Fajr };
        var b = new PrayerEvent { MosqueSlug = "two", MosqueDate = DateTime.Today, Prayer = Prayer.Fajr };
        Check(a.Key != b.Key, "different mosques have independent replay protection");
        table.Calendar = new string[12][][];
        table.IqamaCalendar = new string[12][][];
        var date = new DateTime(2026, 3, 29);
        table.Calendar[2] = new string[31][];
        table.IqamaCalendar[2] = new string[31][];
        table.Calendar[2][28] = new[] { "01:50", "06:00", "12:00", "15:00", "18:00", "23:50" };
        table.IqamaCalendar[2][28] = new[] { "+30", "+0", "+10", "+5", "00:10" };
        var day = PrayerSchedule.Day(table, date);
        Check(day[0].IqamaClock == "03:20", "iqama display follows actual elapsed time across DST");
        Check(day[2].Iqama == day[2].Adhan, "zero-minute iqama is supported");
        Check(day[5].Iqama.Value.Date == date.AddDays(1), "fixed iqama can cross midnight");
        table.Calendar[2][28][5] = "00:30";
        table.IqamaCalendar[2][28][4] = "+10";
        table.Calendar[2][29] = new[] { "05:00", "06:00", "12:00", "15:00", "18:00", "00:30" };
        var overnight = PrayerSchedule.Day(table, date).Single(p => p.Prayer == Prayer.Isha);
        Check(overnight.Adhan.Date == date.AddDays(1) && overnight.Iqama.Value.Date == date.AddDays(1), "after-midnight Isha and iqama use the following date");
        var beforeMidnight = PrayerSchedule.ToInstant(date.AddHours(23).AddMinutes(55), berlin);
        Check(PrayerSchedule.Next(table, beforeMidnight).Adhan == overnight.Adhan &&
            PrayerSchedule.Next(table, beforeMidnight.AddMinutes(15)).Adhan == overnight.Adhan &&
            PrayerSchedule.Current(table, overnight.Adhan.AddMinutes(1)).Adhan == overnight.Adhan,
            "next and current prayer find the previous timetable day's overnight Isha");
    }

    private static void UiChecks()
    {
        Application.EnableVisualStyles();
        Dpi.Init();
        var settings = new AppSettings { Volume = 0, DuckOtherAudio = false, AdhanVoice = Path.Combine(AppPaths.AppDir, "audio/bip.mp3") };
        using var form = new MainForm(settings, false);
        typeof(MainForm).GetMethod("TestAdhan", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        var playback = (AdhanPlayback)typeof(MainForm).GetField("_playback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        Check(playback.IsBusy, "Test adhan works without a prayer or selected mosque");
        playback.Stop();
        using var dialog = new SettingsForm(settings, playback);
        using var picker = new MosquePickerForm(null);
        Check(form.Controls.Count > 0 && dialog.Controls.Count > 0 && picker.Controls.Count > 0, "all three windows construct successfully");
    }

    private static async Task Until(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("Playback did not reach expected state");
            await Task.Delay(10);
        }
    }

    private static async Task PlaybackChecks()
    {
        var settings = new AppSettings { FadeOutSeconds = 1, FadeInSeconds = 1, ResumeDelaySeconds = 120 };
        var audio = new FakeEnvironment(); var player = new FakePlayer();
        using (var playback = new AdhanPlayback(player, audio))
        {
            Check(playback.Start("file", settings, "test") && playback.IsBusy && !playback.Start("file", settings, "second"), "playback immediately reports busy and rejects overlap");
            await Until(() => audio.Volume < .7f);
            playback.Stop(); await Until(() => !playback.IsBusy);
            Check(player.Starts == 0 && audio.Toggles == 0 && Math.Abs(audio.Volume - .7f) < .001f, "Stop during fade never plays or pauses and restores volume");
        }
        settings.FadeOutSeconds = 0;
        audio = new FakeEnvironment(); player = new FakePlayer();
        using (var playback = new AdhanPlayback(player, audio))
        {
            playback.Start("file", settings, "test");
            await Until(() => playback.Phase == PlaybackPhase.WaitingToResume);
            var watch = Stopwatch.StartNew(); playback.Stop(); await Until(() => !playback.IsBusy);
            Check(watch.ElapsedMilliseconds < 1000 && audio.Toggles == 2 && Math.Abs(audio.Volume - .7f) < .001f, "Stop cancels a 120-second resume delay and restores media immediately");
        }
        audio = new FakeEnvironment(); player = new FakePlayer();
        using (var playback = new AdhanPlayback(player, audio))
        {
            playback.Start("file", settings, "test");
            await Until(() => playback.Phase == PlaybackPhase.WaitingToResume);
            audio.Playing = true; audio.Volume = .35f;
            playback.Stop(); await Until(() => !playback.IsBusy);
            Check(audio.Toggles == 1 && audio.Volume == .35f, "manual resume and user volume changes are preserved");
        }
        audio = new FakeEnvironment(); player = new FakePlayer();
        var shutdown = new AdhanPlayback(player, audio);
        shutdown.Start("file", settings, "test");
        await Until(() => shutdown.Phase == PlaybackPhase.WaitingToResume);
        shutdown.Dispose(); shutdown.Dispose();
        Check(!shutdown.IsBusy && audio.Toggles == 2 && audio.Volume == .7f && !shutdown.Start("file", settings, "late"), "exit restores audio, disposes once, and rejects late playback");
        audio = new FakeEnvironment(); player = new FakePlayer { Error = "decoder failure" };
        using (var playback = new AdhanPlayback(player, audio))
        {
            playback.Start("file", settings, "test"); await Until(() => !playback.IsBusy);
            Check(playback.LastError == "decoder failure" && audio.Toggles == 2 && audio.Volume == .7f, "decoder failure is exposed and restores media without waiting");
        }
        settings.ResumeDelaySeconds = 0; settings.FadeInSeconds = .15;
        audio = new FakeEnvironment(); player = new FakePlayer();
        using (var playback = new AdhanPlayback(player, audio))
        {
            playback.Start("file", settings, "test"); await Until(() => !playback.IsBusy);
            Check(audio.Toggles == 2 && audio.Volume == .7f && player.Starts == 1, "normal fade/pause/play/resume sequence completes");
        }
    }

    private static void AudioCheck()
    {
        using var player = new AudioPlayer();
        var error = Task.Run(() => player.Play(Path.Combine(AppPaths.AppDir, "audio/bip.mp3"), 35)).GetAwaiter().GetResult();
        Check(error == null, "native MCI opens and plays MP3 from thread pool through STA pump");
        Check(player.IsPlaying, "native MCI reports active playback");
        var producedSound = false;
        for (var i = 0; i < 20 && !producedSound; i++)
        {
            Thread.Sleep(40);
            producedSound = SystemAudio.IsOwnAudioPlaying();
        }
        Check(producedSound, "native audio session produces nonzero audio samples");
        player.Stop();
        Check(!player.IsPlaying, "native playback stops and closes");
    }

    private static async Task DownloadCheck()
    {
        var file = await MawaqitClient.DownloadVoiceAsync("adhan-afassy");
        Check(File.Exists(file) && new FileInfo(file).Length > 1000, "voice downloads to isolated user folder");
        Check(!Directory.GetFiles(AppPaths.UserAudioDir, "*.part").Any(), "download leaves no temporary files");
        using var player = new AudioPlayer();
        Check(player.Play(file, 0) == null, "downloaded voice decodes with Windows MCI");
        player.Stop();
        var voice = AudioLibrary.Import(file);
        Check(voice.Id == "adhan-afassy" && AppPaths.ResolveAudio(voice.Id) == file, "import and voice resolution work");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { await MawaqitClient.DownloadVoiceAsync("adhan-egypt", ct: cts.Token); Check(false, "cancelled download"); }
        catch (OperationCanceledException) { Check(!Directory.GetFiles(AppPaths.UserAudioDir, "*.part").Any(), "cancelled download cleans temporary files"); }
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public int Starts;
        public string Error;
        public bool IsPlaying => false;
        public string Play(string path, int volumePercent) { Starts++; return Error; }
        public void Stop() { }
        public void SetVolume(int percent) { }
        public void Dispose() { }
    }
    private sealed class FakeEnvironment : IPlaybackEnvironment
    {
        public volatile float Volume = .7f;
        public volatile bool Playing = true;
        public int Toggles;
        public float? GetVolume() => Volume;
        public void SetVolume(float value) => Volume = value;
        public bool IsMuted() => false;
        public bool OtherAudioPlaying() => Playing;
        public void ToggleMedia() { Toggles++; Playing = !Playing; }
    }
}
