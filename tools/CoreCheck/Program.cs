using MawaqitAdhan.Core;

var failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
    if (!ok) failures++;
}

// "dotnet run" forwards its own switches, so only take real slugs
var named = args.Where(a => !a.StartsWith("-")).ToArray();
var slugs = named.Length > 0 ? named : new[] { "omar-witten" };

foreach (var slug in slugs)
{
    Console.WriteLine($"\n=== {slug} ===");
    Timetable t;
    try
    {
        t = await MawaqitClient.FetchTimetableAsync(slug);
    }
    catch (Exception ex)
    {
        Console.WriteLine("  FETCH FAILED: " + ex.Message);
        failures++;
        continue;
    }

    Console.WriteLine($"  name      : {t.Name}");
    Console.WriteLine($"  timezone  : {t.Timezone} -> {t.ResolvedTimeZone.Id} (remote: {t.IsRemoteTimezone})");
    Console.WriteLine($"  jumua     : {t.Jumua} / asDuhr={t.JumuaAsDuhr}");
    Console.WriteLine($"  calendar  : {(t.Calendar == null ? "none" : t.Calendar.Length + " months")}");

    Check(!string.IsNullOrWhiteSpace(t.Name), "has a name");
    Check(t.Calendar != null, "has a yearly calendar");

    var today = t.MosqueToday();
    var day = PrayerSchedule.Day(t, today);
    Console.WriteLine($"  today     : {today:yyyy-MM-dd} ({day.Count} prayers)");
    foreach (var p in day)
        Console.WriteLine($"     {p.Name,-8} {p.AdhanClock}  iqama {p.IqamaClock ?? "-",-6} -> {p.Adhan:yyyy-MM-dd HH:mm zzz}  key={p.Key}");

    Check(day.Count >= 5, "today has at least 5 prayers");
    Check(day.Select(p => p.Adhan).SequenceEqual(day.Select(p => p.Adhan).OrderBy(x => x)), "today's prayers are in order");

    var next = PrayerSchedule.Next(t, DateTimeOffset.Now);
    var cur = PrayerSchedule.Current(t, DateTimeOffset.Now);
    Console.WriteLine($"  next      : {next?.Name} at {next?.Adhan:HH:mm zzz} (in {(next == null ? TimeSpan.Zero : next.Adhan - DateTimeOffset.Now):hh\\:mm\\:ss})");
    Console.WriteLine($"  current   : {cur?.Name} at {cur?.Adhan:HH:mm zzz}");
    Check(next != null && next.Adhan > DateTimeOffset.Now, "next prayer is in the future");
    Check(cur != null && cur.Adhan <= DateTimeOffset.Now, "current prayer is in the past");

    // every day of the year must resolve
    var missing = new List<string>();
    var badOrder = new List<string>();
    for (var d = new DateTime(today.Year, 1, 1); d.Year == today.Year; d = d.AddDays(1))
    {
        var prayers = PrayerSchedule.Day(t, d);
        if (prayers.Count < 5) { missing.Add(d.ToString("MM-dd")); continue; }
        var instants = prayers.Select(p => p.Adhan).ToList();
        for (var i = 1; i < instants.Count; i++)
            if (instants[i] < instants[i - 1]) badOrder.Add(d.ToString("MM-dd"));
    }
    Console.WriteLine($"  year scan : {missing.Count} days missing, {badOrder.Count} out of order");
    if (missing.Count > 0) Console.WriteLine("     missing: " + string.Join(",", missing.Take(12)));
    if (badOrder.Count > 0) Console.WriteLine("     badorder: " + string.Join(",", badOrder.Take(12)));
    Check(missing.Count <= 1, "at most one day of the year is missing (leap day)");
    Check(badOrder.Count == 0, "no day has prayers out of order");

    // DST boundaries for a European masjid
    foreach (var probe in new[] { new DateTime(today.Year, 3, 29), new DateTime(today.Year, 10, 25) })
    {
        var prayers = PrayerSchedule.Day(t, probe);
        if (prayers.Count == 0) continue;
        Console.WriteLine($"  dst {probe:MM-dd} : " + string.Join("  ", prayers.Select(p => $"{p.AdhanClock}->{p.Adhan:HH:mm zzz}")));
    }

    // cache round-trip
    AppPaths.EnsureCreated();
    t.SaveCache();
    var reloaded = Timetable.LoadCache(t.Slug);
    Check(reloaded != null, "timetable cache round-trips");
    if (reloaded != null)
    {
        var a = PrayerSchedule.Day(t, today).Select(p => p.Adhan).ToList();
        var b = PrayerSchedule.Day(reloaded, today).Select(p => p.Adhan).ToList();
        Check(a.SequenceEqual(b), "cached timetable gives the same times");
    }

    Console.WriteLine($"  hijri     : {HijriDate.Format(today, t.HijriAdjustment)}");
}

// ---- unit-ish checks that need no network ----
Console.WriteLine("\n=== helpers ===");
Check(MawaqitClient.ExtractSlug("https://mawaqit.net/de/omar-witten") == "omar-witten", "slug from a de link");
Check(MawaqitClient.ExtractSlug("https://mawaqit.net/en/omar-witten/") == "omar-witten", "slug from a trailing-slash link");
Check(MawaqitClient.ExtractSlug("http://mawaqit.net/fr/grande-mosquee-de-paris") == "grande-mosquee-de-paris", "slug from an fr link");
Check(MawaqitClient.ExtractSlug("omar-witten") == "omar-witten", "bare slug passes through");
Check(MawaqitClient.ExtractSlug("  omar-witten  ") == "omar-witten", "slug is trimmed");

Check(Timetable.ParseClock("05:19") == new TimeSpan(5, 19, 0), "parses 05:19");
Check(Timetable.ParseClock("5:9") == new TimeSpan(5, 9, 0), "parses 5:9");
Check(Timetable.ParseClock("-----") == null, "rejects -----");
Check(Timetable.ParseClock("") == null, "rejects empty");
Check(Timetable.ParseClock("25:00") == null, "rejects 25:00");

Check(AudioLibrary.PrettyName("adhan-madina-fajr") == "Madina (Fajr)", "pretty name for a known voice");
Check(AudioLibrary.PrettyName("adhan-tunisia") == "Tunisia", "pretty name for a user voice");
Check(AudioLibrary.PrettyName("my_custom_adhan") == "My Custom Adhan", "pretty name for any file");
Check(AudioLibrary.IsFajrVoice("adhan-quds-fajr"), "detects a fajr voice");

var berlin = TimeZones.Find("Europe/Berlin");
// 02:30 on the spring-forward night does not exist
var gap = PrayerSchedule.ToInstant(new DateTime(2026, 3, 29, 2, 30, 0), berlin);
Check(gap.Offset == TimeSpan.FromHours(2), "a time inside the DST gap moves past it");
// 02:30 on the autumn night happens twice; we take the earlier one
var ambiguous = PrayerSchedule.ToInstant(new DateTime(2026, 10, 25, 2, 30, 0), berlin);
Check(ambiguous.Offset == TimeSpan.FromHours(2), "an ambiguous time takes its first occurrence");

var search = await MawaqitClient.SearchAsync("witten");
Console.WriteLine($"  search 'witten' -> {search.Count} results: " + string.Join(", ", search.Take(3).Select(m => m.Slug)));
Check(search.Count > 0, "search returns results");
Check(search.All(m => !string.IsNullOrWhiteSpace(m.Slug)), "every result has a slug");

var near = await MawaqitClient.SearchNearAsync(51.437481, 7.3262647);
Console.WriteLine($"  search near Witten -> {near.Count} results: " + string.Join(", ", near.Take(3).Select(m => $"{m.Slug}({m.Proximity}m)")));
Check(near.Count > 0, "proximity search returns results");

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
