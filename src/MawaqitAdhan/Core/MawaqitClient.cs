using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MawaqitAdhan.Core;

public sealed class MosqueSearchResult
{
    public string Uuid { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Label { get; set; }
    public string Localisation { get; set; }
    public string Site { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? Proximity { get; set; }
    public string[] Times { get; set; }
    public string Jumua { get; set; }
    public bool? Closed { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Label) ? Name : Label;
    public override string ToString() => Title;

    public string Subtitle
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(Localisation)) bits.Add(Localisation.Trim());
            if (Proximity is > 0)
                bits.Add(Proximity < 1000
                    ? $"{Proximity} m away"
                    : $"{Proximity.Value / 1000.0:0.#} km away");
            return string.Join("   -   ", bits);
        }
    }
}

/// <summary>
/// Talks to mawaqit.net: the public mosque search endpoint, and the masjid page
/// whose embedded confData block holds the full-year timetable.
/// </summary>
public static class MawaqitClient
{
    public const string BaseUrl = "https://mawaqit.net";
    public const string CdnUrl = "https://cdn.mawaqit.net";

    private static readonly HttpClient Http = CreateClient();


    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MawaqitAdhan/1.0");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en,ar;q=0.8");
        return http;
    }

    /// <summary>Accepts a slug, or any mawaqit URL, and returns the slug.</summary>
    public static string ExtractSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        if (input.IndexOf("mawaqit.net", StringComparison.OrdinalIgnoreCase) >= 0 || input.Contains("://"))
        {
            if (!input.Contains("://")) input = "https://" + input;
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                !string.Equals(uri.Host, "mawaqit.net", StringComparison.OrdinalIgnoreCase)) return null;
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            input = parts.Length == 2 && Regex.IsMatch(parts[0], @"^[a-z]{2}$") ? parts[1]
                : parts.Length == 1 ? parts[0] : "";
        }

        input = input.Trim('/');
        return Regex.IsMatch(input, @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,199}$") ? input : null;
    }

    // ---- search --------------------------------------------------------

    public static Task<List<MosqueSearchResult>> SearchAsync(string word, CancellationToken ct = default)
        => GetSearchAsync($"{BaseUrl}/api/2.0/mosque/search?word={Uri.EscapeDataString(word ?? "")}", ct);

    public static Task<List<MosqueSearchResult>> SearchNearAsync(double lat, double lon, CancellationToken ct = default)
        => GetSearchAsync(
            $"{BaseUrl}/api/2.0/mosque/search?lat={lat.ToString(CultureInfo.InvariantCulture)}" +
            $"&lon={lon.ToString(CultureInfo.InvariantCulture)}", ct);

    private static async Task<List<MosqueSearchResult>> GetSearchAsync(string url, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var list = Json.Read<List<MosqueSearchResult>>(json) ?? new List<MosqueSearchResult>();
        return list.Where(m => !string.IsNullOrWhiteSpace(m.Slug)).ToList();
    }

    // ---- timetable -----------------------------------------------------

    public static async Task<Timetable> FetchTimetableAsync(string slug, CancellationToken ct = default)
    {
        slug = ExtractSlug(slug);
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("No masjid selected.", nameof(slug));

        var url = $"{BaseUrl}/en/{Uri.EscapeDataString(slug)}";
        using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"mawaqit has no masjid at \"{slug}\".");
        resp.EnsureSuccessStatusCode();

        var html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var conf = ExtractConfData(html)
                   ?? throw new InvalidOperationException("The masjid page did not contain any prayer times.");

        var t = ParseConfData(conf, slug);
        // Reject unusable responses before replacing a working offline cache.
        if (PrayerSchedule.Day(t, t.MosqueToday()).Count < 5)
            throw new InvalidOperationException("The masjid did not publish a complete timetable for today.");
        Log.Info($"Fetched timetable for {slug} ({t.Name})");
        return t;
    }

    /// <summary>Pulls the "confData = {...};" JSON object out of the page.</summary>
    internal static string ExtractConfData(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var m = Regex.Match(html, @"confData\s*=\s*\{");
        if (!m.Success) return null;

        var start = html.IndexOf('{', m.Index);
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return html.Substring(start, i - start + 1);
                    break;
            }
        }
        return null;
    }

    internal static Timetable ParseConfData(string json, string slug)
    {
        var root = Json.Object(json);

        var t = new Timetable
        {
            Slug = slug,
            Name = Str(root, "name") ?? Str(root, "label") ?? slug,
            Timezone = Str(root, "timezone"),
            CountryCode = Str(root, "countryCode"),
            Site = Str(root, "site"),
            Latitude = Num(root, "latitude"),
            Longitude = Num(root, "longitude"),
            Jumua = Str(root, "jumua"),
            Jumua2 = Str(root, "jumua2"),
            Jumua3 = Str(root, "jumua3"),
            JumuaAsDuhr = Bool(root, "jumuaAsDuhr"),
            HijriAdjustment = (int)Num(root, "hijriAdjustment"),
            Calendar = ParseCalendar(root, "calendar"),
            IqamaCalendar = ParseCalendar(root, "iqamaCalendar"),
            FetchedUtc = DateTime.UtcNow
        };

        // Today's times, straight off the page - used if the yearly calendar is absent.
        var times = StrArray(root, "times");
        var shuruq = Str(root, "shuruq");
        if (times is { Length: >= 5 })
        {
            t.FallbackTimes = new[] { times[0], shuruq, times[1], times[2], times[3], times[4] };
            t.FallbackDate = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, t.ResolvedTimeZone).Date.ToString("yyyy-MM-dd");
        }

        return t;
    }

    private static string[][][] ParseCalendar(Dictionary<string, object> root, string name)
    {
        if (!root.TryGetValue(name, out var raw) || raw is not object[] cal) return null;
        var months = new string[12][][];
        for (var m = 0; m < Math.Min(12, cal.Length); m++)
        {
            var days = months[m] = new string[31][];
            if (cal[m] is Dictionary<string, object> month)
            {
                foreach (var day in month)
                    if (int.TryParse(day.Key, out var n) && n >= 1 && n <= 31)
                        days[n - 1] = StrArray(day.Value);
            }
            else if (cal[m] is object[] array)
                for (var d = 0; d < Math.Min(31, array.Length); d++) days[d] = StrArray(array[d]);
        }
        return cal.Length == 0 ? null : months;
    }

    private static string Str(Dictionary<string, object> e, string name) =>
        e.TryGetValue(name, out var v) ? v as string : null;

    private static double Num(Dictionary<string, object> e, string name) =>
        e.TryGetValue(name, out var v) && double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && !double.IsInfinity(d) ? d : 0;

    private static bool Bool(Dictionary<string, object> e, string name) =>
        e.TryGetValue(name, out var v) && bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var b) && b;

    private static string[] StrArray(Dictionary<string, object> e, string name) =>
        e.TryGetValue(name, out var v) ? StrArray(v) : null;

    private static string[] StrArray(object value) => value is object[] array
        ? array.Select(v => v is string || v is ValueType ? Convert.ToString(v, CultureInfo.InvariantCulture) : null).ToArray()
        : null;

    // ---- adhan voices available on the mawaqit CDN ---------------------

    public static readonly (string Id, string Title)[] DownloadableVoices =
    {
        ("adhan-madina",       "Madina"),
        ("adhan-madina-fajr",  "Madina (Fajr)"),
        ("adhan-maquah",       "Makkah"),
        ("adhan-maquah-fajr",  "Makkah (Fajr)"),
        ("adhan-afassy",       "Mishary Alafasy"),
        ("adhan-afassy-fajr",  "Mishary Alafasy (Fajr)"),
        ("adhan-egypt",        "Egypt"),
        ("adhan-egypt-fajr",   "Egypt (Fajr)"),
        ("adhan-quds",         "Al-Quds"),
        ("adhan-quds-fajr",    "Al-Quds (Fajr)"),
        ("adhan-algeria",      "Algeria"),
        ("adhan-algeria-fajr", "Algeria (Fajr)")
    };

    public static async Task<string> DownloadVoiceAsync(string id, IProgress<int> progress = null,
        CancellationToken ct = default)
    {
        if (!DownloadableVoices.Any(v => v.Id == id)) throw new ArgumentException("Unknown adhan voice.", nameof(id));
        AppPaths.EnsureCreated();
        var target = Path.Combine(AppPaths.UserAudioDir, id + ".mp3");
        var tmp = target + "." + Guid.NewGuid().ToString("N") + ".part";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        ct = timeout.Token;
        try
        {
        using (var resp = await Http.GetAsync($"{CdnUrl}/audio/{id}.mp3", HttpCompletionOption.ResponseHeadersRead, ct)
                   .ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;

            using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var cancelRead = ct.Register(() => src.Dispose());
            using (var dst = File.Create(tmp))
            {
                var buffer = new byte[64 * 1024];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                    read += n;
                    if (read > 32 * 1024 * 1024) throw new IOException("The audio download is too large.");
                    if (total > 0) progress?.Report((int)(read * 100 / total));
                }
                if (read == 0 || (total > 0 && total != read)) throw new IOException("The audio download is incomplete.");
            }
        }

        ct.ThrowIfCancellationRequested();
        AtomicFile.Replace(tmp, target);
        Log.Info($"Downloaded adhan voice {id}");
        return target;
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
