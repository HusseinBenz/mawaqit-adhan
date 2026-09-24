namespace MawaqitAdhan.Core;

/// <summary>Everything the app reads or writes on disk, in one place.</summary>
public static class AppPaths
{
    public static string AppDir { get; } = AppContext.BaseDirectory;

    public static string DataDir { get; } = Environment.GetEnvironmentVariable("MAWAQIT_ADHAN_DATA_DIR") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MawaqitAdhan");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string CacheDir => Path.Combine(DataDir, "cache");
    public static string LogFile => Path.Combine(DataDir, "mawaqit-adhan.log");

    /// <summary>Adhan voices shipped with the app.</summary>
    public static string BundledAudioDir => Path.Combine(AppDir, "audio");

    /// <summary>Adhan voices the user downloaded or added.</summary>
    public static string UserAudioDir => Path.Combine(DataDir, "audio");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(UserAudioDir);
    }

    public static string TimetableCacheFile(string slug)
    {
        var safe = string.Join("_", (slug ?? "unknown").Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(CacheDir, safe + ".json");
    }

    /// <summary>Resolves an adhan name ("adhan-madina") or a full path to a playable file.</summary>
    public static string ResolveAudio(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
        if (Path.IsPathRooted(nameOrPath))
            return File.Exists(nameOrPath) ? nameOrPath : null;

        var file = nameOrPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? nameOrPath : nameOrPath + ".mp3";

        foreach (var dir in new[] { UserAudioDir, BundledAudioDir })
        {
            var p = Path.Combine(dir, file);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
