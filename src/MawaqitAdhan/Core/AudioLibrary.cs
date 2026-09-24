using System.Globalization;

namespace MawaqitAdhan.Core;

public sealed class AdhanVoice
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Path { get; set; }
    public bool Bundled { get; set; }

    public override string ToString() => Title;
}

/// <summary>
/// Every adhan the app can play: the mp3 files shipped next to the exe plus
/// anything the user downloaded or dropped into their own audio folder.
/// </summary>
public static class AudioLibrary
{
    /// <summary>Voices on disk, user folder first so a downloaded file shadows a bundled one.</summary>
    public static List<AdhanVoice> Available()
    {
        var found = new Dictionary<string, AdhanVoice>(StringComparer.OrdinalIgnoreCase);

        foreach (var (dir, bundled) in new[] { (AppPaths.BundledAudioDir, true), (AppPaths.UserAudioDir, false) })
        {
            List<string> files;
            try
            {
                if (!Directory.Exists(dir)) continue;
                files = Directory.EnumerateFiles(dir, "*.mp3").ToList();
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not list {dir}: {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                var id = System.IO.Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (id.Equals("bip", StringComparison.OrdinalIgnoreCase)) continue;

                found[id] = new AdhanVoice { Id = id, Title = PrettyName(id), Path = file, Bundled = bundled };
            }
        }

        return found.Values.OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string AnyAvailableFile() => Available().FirstOrDefault()?.Path;

    /// <summary>Voices mawaqit hosts that are not on this machine yet.</summary>
    public static List<(string Id, string Title)> Downloadable()
    {
        var have = new HashSet<string>(Available().Select(v => v.Id), StringComparer.OrdinalIgnoreCase);
        return MawaqitClient.DownloadableVoices
            .Where(v => !have.Contains(v.Id))
            .Select(v => (v.Id, v.Title))
            .ToList();
    }

    /// <summary>Copies an mp3 the user picked into the app's audio folder.</summary>
    public static AdhanVoice Import(string sourcePath)
    {
        AppPaths.EnsureCreated();

        var id = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("That file has no name.", nameof(sourcePath));

        var target = System.IO.Path.Combine(AppPaths.UserAudioDir, id + ".mp3");
        if (!string.Equals(System.IO.Path.GetFullPath(sourcePath), System.IO.Path.GetFullPath(target),
                StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, target, overwrite: true);

        Log.Info($"Imported adhan {id}");
        return new AdhanVoice { Id = id, Title = PrettyName(id), Path = target, Bundled = false };
    }

    public static bool IsFajrVoice(string id) =>
        id != null && id.IndexOf("fajr", StringComparison.OrdinalIgnoreCase) >= 0;

    public static string PrettyName(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";

        foreach (var (knownId, title) in MawaqitClient.DownloadableVoices)
            if (string.Equals(knownId, id, StringComparison.OrdinalIgnoreCase))
                return title;

        var name = id;
        if (name.StartsWith("adhan-", StringComparison.OrdinalIgnoreCase)) name = name.Substring(6);
        else if (name.StartsWith("adhan_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(6);

        var fajr = false;
        if (name.EndsWith("-fajr", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_fajr", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - 5);
            fajr = true;
        }

        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        if (name.Length == 0) name = id;
        name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);

        return fajr ? name + " (Fajr)" : name;
    }
}
