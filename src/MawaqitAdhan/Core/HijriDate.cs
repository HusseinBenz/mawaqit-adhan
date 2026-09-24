using System.Globalization;

namespace MawaqitAdhan.Core;

/// <summary>Hijri date, honouring the masjid's own +/- day adjustment.</summary>
public static class HijriDate
{
    private static readonly UmAlQuraCalendar Calendar = new();

    private static readonly string[] Months =
    {
        "Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Ula", "Jumada al-Akhirah",
        "Rajab", "Shaaban", "Ramadan", "Shawwal", "Dhul Qidah", "Dhul Hijjah"
    };

    public static string Format(DateTime date, int adjustmentDays = 0)
    {
        try
        {
            var d = date.AddDays(adjustmentDays);
            if (d < Calendar.MinSupportedDateTime || d > Calendar.MaxSupportedDateTime) return null;

            var day = Calendar.GetDayOfMonth(d);
            var month = Calendar.GetMonth(d);
            var year = Calendar.GetYear(d);

            if (month < 1 || month > 12) return null;
            return $"{day} {Months[month - 1]} {year} AH";
        }
        catch (Exception ex)
        {
            Log.Warn("Could not work out the hijri date: " + ex.Message);
            return null;
        }
    }
}
