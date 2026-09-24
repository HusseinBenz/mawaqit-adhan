using System.Web.Script.Serialization;

namespace MawaqitAdhan.Core;

public sealed class AppSettings
{
    // ---- masjid -------------------------------------------------------
    public string MosqueSlug { get; set; }
    public string MosqueName { get; set; }
    public string MosqueLocation { get; set; }

    // ---- adhan --------------------------------------------------------
    /// <summary>Fajr, Dhuhr, Asr, Maghrib, Isha.</summary>
    public bool[] AdhanEnabled { get; set; } = { true, true, true, true, true };

    public string AdhanVoice { get; set; } = "adhan-madina";
    public string FajrAdhanVoice { get; set; } = "adhan-madina-fajr";

    /// <summary>Adhan playback volume, 0-100, applied in addition to system volume.</summary>
    public int Volume { get; set; } = 90;

    // ---- other audio: fade out / pause / resume ------------------------
    public bool DuckOtherAudio { get; set; } = true;
    public bool OnlyDuckWhenAudioPlaying { get; set; } = true;
    public double FadeOutSeconds { get; set; } = 2.0;
    public bool PauseOtherAudio { get; set; } = true;
    public double ResumeDelaySeconds { get; set; } = 5.0;
    public double FadeInSeconds { get; set; } = 3.0;

    // ---- notifications ------------------------------------------------
    public bool ShowNotifications { get; set; } = true;
    public bool NotifyBeforeAdhan { get; set; } = false;
    public int NotifyBeforeMinutes { get; set; } = 10;
    public bool NotifyAtIqama { get; set; } = false;

    // ---- windows behaviour --------------------------------------------
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    // ---- bookkeeping ---------------------------------------------------
    /// <summary>Keys ("slug|2026-09-20|Fajr") of adhans already handled, preventing repeats after restart.</summary>
    public List<string> FiredKeys { get; set; } = new();

    public DateTime? LastFetchUtc { get; set; }

    [ScriptIgnore]
    public bool HasMosque => !string.IsNullOrWhiteSpace(MosqueSlug);

    // -------------------------------------------------------------------

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = Json.Read<AppSettings>(
                    File.ReadAllText(AppPaths.SettingsFile));
                if (s != null) { s.Normalise(); return s; }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, starting from defaults", ex);
        }
        var fresh = new AppSettings();
        fresh.Normalise();
        return fresh;
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            TrimFiredKeys();
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, Json.Write(this));
            AtomicFile.Replace(tmp, AppPaths.SettingsFile);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    private void Normalise()
    {
        MosqueSlug = MawaqitClient.ExtractSlug(MosqueSlug);
        if (string.IsNullOrWhiteSpace(AdhanVoice)) AdhanVoice = "adhan-madina";
        if (string.IsNullOrWhiteSpace(FajrAdhanVoice)) FajrAdhanVoice = "adhan-madina-fajr";
        if (AdhanEnabled == null || AdhanEnabled.Length != 5)
        {
            var e = new bool[5];
            for (var i = 0; i < 5; i++) e[i] = AdhanEnabled == null || i >= AdhanEnabled.Length || AdhanEnabled[i];
            AdhanEnabled = e;
        }
        Volume = Numbers.Clamp(Volume, 0, 100);
        NotifyBeforeMinutes = Numbers.Clamp(NotifyBeforeMinutes, 1, 60);
        FadeOutSeconds = Numbers.Clamp(FadeOutSeconds, 0, 30);
        FadeInSeconds = Numbers.Clamp(FadeInSeconds, 0, 30);
        ResumeDelaySeconds = Numbers.Clamp(ResumeDelaySeconds, 0, 120);
        FiredKeys ??= new List<string>();
        // Retain the old installation's replay protection when adding mosque-specific keys.
        FiredKeys = FiredKeys.Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Count(c => c == '|') == 1 && HasMosque ? MosqueSlug + "|" + k : k)
            .Distinct().ToList();
        TrimFiredKeys();
    }

    /// <summary>Keeps only the last few days of "already played" markers.</summary>
    private void TrimFiredKeys()
    {
        if (FiredKeys == null) { FiredKeys = new List<string>(); return; }
        if (FiredKeys.Count <= 40) return;
        FiredKeys = FiredKeys.Skip(FiredKeys.Count - 40).ToList();
    }
}
