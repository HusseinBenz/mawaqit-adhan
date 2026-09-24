using System.Globalization;

namespace MawaqitAdhan.Core;

public sealed class PrayerEvent
{
    public string MosqueSlug { get; set; }
    public Prayer Prayer { get; set; }

    /// <summary>The masjid-local calendar date this prayer belongs to.</summary>
    public DateTime MosqueDate { get; set; }

    /// <summary>The exact instant of the adhan, timezone-correct.</summary>
    public DateTimeOffset Adhan { get; set; }

    public DateTimeOffset? Iqama { get; set; }

    /// <summary>The time as the masjid prints it, e.g. "05:19".</summary>
    public string AdhanClock { get; set; }

    public string IqamaClock { get; set; }

    public string Name => Timetable.PrayerNames[(int)Prayer];
    public string NameArabic => Timetable.PrayerNamesArabic[(int)Prayer];

    /// <summary>Stable id used to remember that this adhan already played.</summary>
    public string Key => $"{MosqueSlug}|{MosqueDate:yyyy-MM-dd}|{Prayer}";

    /// <summary>Sunrise is shown but never called.</summary>
    public bool CallsAdhan => Prayer != Prayer.Sunrise;

    /// <summary>Index into AppSettings.AdhanEnabled (Fajr..Isha), or -1 for sunrise.</summary>
    public int AdhanSlot => Prayer switch
    {
        Prayer.Fajr => 0,
        Prayer.Dhuhr => 1,
        Prayer.Asr => 2,
        Prayer.Maghrib => 3,
        Prayer.Isha => 4,
        _ => -1
    };
}

public static class PrayerSchedule
{
    /// <summary>Every prayer of one masjid-local day, in order.</summary>
    public static List<PrayerEvent> Day(Timetable t, DateTime mosqueDate)
    {
        var result = new List<PrayerEvent>(6);
        if (t == null) return result;

        var times = t.TimesFor(mosqueDate);
        if (times == null) return result;

        var iqamas = t.IqamaFor(mosqueDate);
        var tz = t.ResolvedTimeZone;
        var prayerDate = mosqueDate.Date;
        TimeSpan? previousClock = null;

        for (var i = 0; i < 6 && i < times.Length; i++)
        {
            var clock = Timetable.ParseClock(times[i]);
            if (clock == null) continue;

            if (previousClock.HasValue && clock.Value < previousClock.Value) prayerDate = prayerDate.AddDays(1);
            previousClock = clock;
            var adhan = ToInstant(prayerDate.Add(clock.Value), tz);

            DateTimeOffset? iqama = null;
            string iqamaClock = null;

            // iqamaCalendar has 5 entries (no sunrise), so shift the index past it
            var iqamaIndex = i == 0 ? 0 : i - 1;
            if (i != (int)Prayer.Sunrise && iqamas != null && iqamaIndex < iqamas.Length)
            {
                var parsed = ResolveIqama(iqamas[iqamaIndex], prayerDate, clock.Value, tz);
                iqama = parsed.Instant;
                iqamaClock = parsed.Clock;
            }

            result.Add(new PrayerEvent
            {
                MosqueSlug = t.Slug,
                Prayer = (Prayer)i,
                MosqueDate = mosqueDate,
                Adhan = adhan,
                Iqama = iqama,
                AdhanClock = Format(clock.Value),
                IqamaClock = iqamaClock
            });
        }

        return result.OrderBy(p => p.Adhan).ToList();
    }

    /// <summary>All prayers between two instants (inclusive), crossing day boundaries.</summary>
    public static IEnumerable<PrayerEvent> Between(Timetable t, DateTimeOffset from, DateTimeOffset to)
    {
        if (t == null || to < from) yield break;

        var tz = t.ResolvedTimeZone;
        var firstDate = TimeZoneInfo.ConvertTime(from, tz).Date.AddDays(-1);
        var lastDate = TimeZoneInfo.ConvertTime(to, tz).Date.AddDays(1);

        for (var d = firstDate; d <= lastDate; d = d.AddDays(1))
            foreach (var p in Day(t, d))
                if (p.Adhan >= from && p.Adhan <= to)
                    yield return p;
    }

    /// <summary>The next prayer strictly after <paramref name="now"/>, looking up to a week ahead.</summary>
    public static PrayerEvent Next(Timetable t, DateTimeOffset now, bool adhanOnly = false)
    {
        if (t == null) return null;
        var tz = t.ResolvedTimeZone;
        var date = TimeZoneInfo.ConvertTime(now, tz).Date;

        PrayerEvent best = null;
        for (var i = -1; i < 8; i++)
        {
            foreach (var p in Day(t, date.AddDays(i)))
            {
                if (p.Adhan <= now) continue;
                if (adhanOnly && !p.CallsAdhan) continue;
                if (best == null || p.Adhan < best.Adhan) best = p;
            }
        }
        return best;
    }

    /// <summary>The most recent prayer at or before <paramref name="now"/>.</summary>
    public static PrayerEvent Current(Timetable t, DateTimeOffset now)
    {
        if (t == null) return null;
        var tz = t.ResolvedTimeZone;
        var date = TimeZoneInfo.ConvertTime(now, tz).Date;

        PrayerEvent best = null;
        for (var i = 0; i < 8; i++)
        {
            foreach (var p in Day(t, date.AddDays(-i)))
                if (p.Adhan <= now && (best == null || p.Adhan > best.Adhan)) best = p;
        }
        return best;
    }

    /// <summary>
    /// Turns a masjid wall-clock time into a real instant, surviving both DST jumps:
    /// a time inside the spring-forward gap is pushed past it, and an ambiguous
    /// autumn time resolves to its first (daylight) occurrence.
    /// </summary>
    public static DateTimeOffset ToInstant(DateTime wallClock, TimeZoneInfo tz)
    {
        wallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        if (tz.IsInvalidTime(wallClock))
        {
            var before = wallClock;
            var after = wallClock;
            for (var i = 0; i < 2880 && tz.IsInvalidTime(before); i++) before = before.AddMinutes(-1);
            for (var i = 0; i < 2880 && tz.IsInvalidTime(after); i++) after = after.AddMinutes(1);
            wallClock = wallClock.Add(tz.GetUtcOffset(after) - tz.GetUtcOffset(before));
        }

        if (tz.IsAmbiguousTime(wallClock))
        {
            var offsets = tz.GetAmbiguousTimeOffsets(wallClock);
            var pick = offsets[0];
            foreach (var o in offsets) if (o > pick) pick = o; // larger offset = earlier instant
            return new DateTimeOffset(wallClock, pick);
        }

        return new DateTimeOffset(wallClock, tz.GetUtcOffset(wallClock));
    }

    /// <summary>mawaqit writes iqama either as "+25" (minutes after adhan) or as a fixed "13:45".</summary>
    private static (DateTimeOffset? Instant, string Clock) ResolveIqama(
        string raw, DateTime mosqueDate, TimeSpan adhanClock, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        raw = raw.Trim();

        if (raw.Contains(':'))
        {
            var fixedClock = Timetable.ParseClock(raw);
            if (fixedClock == null) return (null, null);

            var date = mosqueDate;
            // an iqama printed as an earlier clock time than the adhan belongs to the next day (Isha past midnight)
            if (fixedClock.Value < adhanClock && adhanClock - fixedClock.Value > TimeSpan.FromHours(12))
                date = date.AddDays(1);

            return (ToInstant(date.Add(fixedClock.Value), tz), Format(fixedClock.Value));
        }

        var digits = raw.TrimStart('+');
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return (null, null);
        if (minutes < 0 || minutes > 1440) return (null, null);

        var instant = ToInstant(mosqueDate.Add(adhanClock), tz).AddMinutes(minutes);
        return (instant, TimeZoneInfo.ConvertTime(instant, tz).ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalHours % 24:00}:{t.Minutes:00}";
}
