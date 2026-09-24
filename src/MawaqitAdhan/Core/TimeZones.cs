namespace MawaqitAdhan.Core;

/// <summary>CLDR maps IANA names to the DST rules maintained by Windows.</summary>
public static class TimeZones
{
    private static readonly Lazy<Dictionary<string, string>> Map = new(Load);

    public static TimeZoneInfo Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new TimeZoneNotFoundException("The masjid has no time zone.");
        if (Map.Value.TryGetValue(id, out var windows)) id = windows;
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(TimeZones).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("windows-zones.tsv", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var fields = line.Split('\t');
            if (fields.Length == 2) map[fields[0]] = fields[1];
        }
        return map;
    }
}
