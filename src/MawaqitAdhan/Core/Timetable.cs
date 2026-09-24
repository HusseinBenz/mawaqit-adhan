using System.Globalization;
using System.Web.Script.Serialization;

namespace MawaqitAdhan.Core;

public enum Prayer { Fajr = 0, Sunrise = 1, Dhuhr = 2, Asr = 3, Maghrib = 4, Isha = 5 }

/// <summary>
/// A masjid's whole-year timetable as published by mawaqit. Cached to disk, so the
/// app keeps working offline for the rest of the year.
/// </summary>
public sealed class Timetable
{
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Location { get; set; }
    public string Timezone { get; set; }
    public string CountryCode { get; set; }
    public string Site { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string Jumua { get; set; }
    public string Jumua2 { get; set; }
    public string Jumua3 { get; set; }
    public bool JumuaAsDuhr { get; set; }

    public int HijriAdjustment { get; set; }

    /// <summary>[month 0-11][day 0-30][Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha]</summary>
    public string[][][] Calendar { get; set; }

    /// <summary>[month 0-11][day 0-30][Fajr..Isha] - either "+25" (minutes after adhan) or "13:45".</summary>
    public string[][][] IqamaCalendar { get; set; }

    /// <summary>Times for the day the page was fetched - a fallback when the calendar is missing.</summary>
    public string[] FallbackTimes { get; set; }
    public string FallbackDate { get; set; }

    public DateTime FetchedUtc { get; set; }

    public static readonly string[] PrayerNames = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };
    public static readonly string[] PrayerNamesArabic = { "الفجر", "الشروق", "الظهر", "العصر", "المغرب", "العشاء" };

    private TimeZoneInfo _tz;

    [ScriptIgnore]
    public TimeZoneInfo ResolvedTimeZone
    {
        get
        {
            if (_tz != null) return _tz;
            if (!string.IsNullOrWhiteSpace(Timezone))
            {
                return _tz = TimeZones.Find(Timezone);
            }
            throw new TimeZoneNotFoundException("The masjid has no time zone. Refresh its timetable.");
        }
    }

    /// <summary>True when the masjid keeps a different clock than this PC.</summary>
    [ScriptIgnore]
    public bool IsRemoteTimezone =>
        ResolvedTimeZone.Id != TimeZoneInfo.Local.Id &&
        ResolvedTimeZone.GetUtcOffset(DateTime.UtcNow) != TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);

    /// <summary>Current date at the masjid.</summary>
    public DateTime MosqueToday() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ResolvedTimeZone).Date;

    /// <summary>
    /// The six times of a given masjid-local date - [Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha] -
    /// or null if that date is not covered. Masjids that publish five times a day (no sunrise)
    /// are widened to the same six-slot shape.
    /// </summary>
    public string[] TimesFor(DateTime date)
    {
        // A previous year's Ramadan overrides must not silently repeat next year.
        if (FetchedUtc != default && date.Year != TimeZoneInfo.ConvertTime(
                new DateTimeOffset(DateTime.SpecifyKind(FetchedUtc, DateTimeKind.Utc)), ResolvedTimeZone).Year)
            return null;
        var fromCalendar = WidenToSix(FromCalendar(Calendar, date));
        if (fromCalendar != null) return fromCalendar;

        if (FallbackTimes is { Length: >= 6 } &&
            DateTime.TryParse(FallbackDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
            d.Date == date.Date)
            return FallbackTimes;

        return null;
    }

    /// <summary>Iqama entries for a date - five slots, Fajr..Isha, no sunrise.</summary>
    public string[] IqamaFor(DateTime date)
    {
        var day = FromCalendar(IqamaCalendar, date);
        return day is { Length: >= 5 } ? day : null;
    }

    private static string[] WidenToSix(string[] day)
    {
        if (day == null) return null;
        if (day.Length >= 6) return day;
        if (day.Length == 5) return new[] { day[0], null, day[1], day[2], day[3], day[4] };
        return null;
    }

    private static string[] FromCalendar(string[][][] cal, DateTime date)
    {
        if (cal == null || cal.Length < 12) return null;
        var month = cal[date.Month - 1];
        if (month == null || month.Length < date.Day) return null;
        return month[date.Day - 1];
    }

    /// <summary>Parses "05:19" into minutes-from-midnight; null for blank / "-----" entries.</summary>
    public static TimeSpan? ParseClock(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        var parts = value.Split(':');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) return null;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) return null;
        if (h < 0 || h > 23 || m < 0 || m > 59) return null;
        return new TimeSpan(h, m, 0);
    }

    // ---- cache ---------------------------------------------------------

    public void SaveCache()
    {
        try
        {
            AppPaths.EnsureCreated();
            var path = AppPaths.TimetableCacheFile(Slug);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Write(this));
            AtomicFile.Replace(tmp, path);
        }
        catch (Exception ex)
        {
            Log.Error("Could not cache timetable", ex);
        }
    }

    public static Timetable LoadCache(string slug)
    {
        try
        {
            var path = AppPaths.TimetableCacheFile(slug);
            if (!File.Exists(path)) return null;
            var table = Json.Read<Timetable>(File.ReadAllText(path));
            if (table == null || table.Slug != slug) return null;
            _ = table.ResolvedTimeZone;
            return table;
        }
        catch (Exception ex)
        {
            Log.Error("Could not read cached timetable", ex);
            return null;
        }
    }
}
