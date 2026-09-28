using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace MawaqitAdhan.Setup;

internal sealed class InstallContext
{
    public string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MawaqitAdhan";
    public string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string RunName = "MawaqitAdhan";
    public string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mawaqit Adhan.lnk");
    public string DefaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MawaqitAdhan");
    public Action StopRunning = RunningApp.Stop;
    public Action<string> AfterCopy { get; set; }
}

internal sealed class InstallManifest
{
    public string Product { get; set; }
    public string Version { get; set; }
    public string[] Files { get; set; }
}

internal sealed class Installer
{
    public const string Version = "1.0.0-beta.2";
    public const string ManifestName = "installation.json";
    private readonly InstallContext _context;
    public Installer(InstallContext context) => _context = context;

    internal static string CommandExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command.Substring(1, end - 1) : null;
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? command.Substring(0, exe + 4) : null;
    }

    internal static bool IsApp(string path)
    {
        try
        {
            return File.Exists(path) && Path.GetFileName(path).Equals("MawaqitAdhan.exe", StringComparison.OrdinalIgnoreCase) &&
                FileVersionInfo.GetVersionInfo(path).ProductName == "Mawaqit Adhan";
        }
        catch { return false; }
    }

    public string DetectDirectory()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(_context.UninstallKey))
        {
            var path = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(path) && IsApp(Path.Combine(path, "MawaqitAdhan.exe"))) return path;
        }
        using (var key = Registry.CurrentUser.OpenSubKey(_context.RunKey))
        {
            var path = CommandExecutable(key?.GetValue(_context.RunName) as string);
            if (IsApp(path)) return Path.GetDirectoryName(path);
        }
        // Tests use an isolated registry and disable process discovery/closing.
        if (_context.StopRunning == (Action)RunningApp.Stop)
        {
            foreach (var path in RunningApp.Paths()) if (IsApp(path)) return Path.GetDirectoryName(path);
        }
        else return _context.DefaultDirectory;
        foreach (var path in new[] { _context.DefaultDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), @"adhan\dist\MawaqitAdhan") })
            if (IsApp(Path.Combine(path, "MawaqitAdhan.exe"))) return path;
        return _context.DefaultDirectory;
    }

    internal static string Inside(string directory, string relative)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid package path: " + relative);
        // Do not write/delete outside the destination through a directory junction.
        for (var parent = Path.GetDirectoryName(path); parent != null && parent.Length >= root.Length; parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The installation contains a linked folder: " + parent);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The installation contains a linked file: " + path);
        return path;
    }

    public void Install(Stream payload, string directory, string setupExecutable)
    {
        directory = Path.GetFullPath(directory);
        if (directory.TrimEnd('\\') == Path.GetPathRoot(directory).TrimEnd('\\'))
            throw new IOException("Choose an application folder, not the root of a drive.");
        var temp = Path.Combine(Path.GetTempPath(), "MawaqitAdhan-setup-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(temp, "stage");
        var backup = Path.Combine(temp, "backup");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(backup);
        var changed = new List<string>();
        var registryBefore = ReadRegistry(_context.UninstallKey);
        var runBefore = ReadRegistry(_context.RunKey);
        var shortcutBefore = File.Exists(_context.Shortcut) ? File.ReadAllBytes(_context.Shortcut) : null;
        var registrationStarted = false;
        var cleanTemporaryFiles = true;
        try
        {
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true))
                foreach (var entry in zip.Entries)
                {
                    var target = Inside(stage, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target);
                }
            var app = Path.Combine(stage, "MawaqitAdhan.exe");
            if (!IsApp(app)) throw new InvalidDataException("The package does not contain Mawaqit Adhan.");
            var installed = Path.Combine(directory, "MawaqitAdhan.exe");
            if (File.Exists(installed) && !IsApp(installed)) throw new IOException("The selected folder contains a different application.");
            if (IsApp(installed) && new System.Version(FileVersionInfo.GetVersionInfo(installed).FileVersion) >
                new System.Version(FileVersionInfo.GetVersionInfo(app).FileVersion))
                throw new IOException("A newer version is already installed. Downgrading is not supported.");
            File.Copy(setupExecutable, Path.Combine(stage, "Uninstall.exe"));
            var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(stage.Length + 1)).ToList();
            files.Add(ManifestName);
            File.WriteAllText(Path.Combine(stage, ManifestName), new JavaScriptSerializer().Serialize(
                new InstallManifest { Product = "MawaqitAdhan", Version = Version, Files = files.ToArray() }));
            // Validate all destination paths before closing or replacing the old app.
            foreach (var file in files) Inside(directory, file);
            _context.StopRunning();
            Directory.CreateDirectory(directory);
            foreach (var file in files)
            {
                var target = Inside(directory, file);
                var saved = Inside(backup, file);
                if (File.Exists(target)) { Directory.CreateDirectory(Path.GetDirectoryName(saved)); File.Copy(target, saved); }
                changed.Add(file);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Inside(stage, file), target, overwrite: true);
                _context.AfterCopy?.Invoke(file);
            }
            registrationStarted = true;
            using (var key = Registry.CurrentUser.CreateSubKey(_context.UninstallKey))
            {
                key.SetValue("DisplayName", "Mawaqit Adhan");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "Hussein Benz");
                key.SetValue("InstallLocation", directory);
                key.SetValue("DisplayIcon", installed);
                key.SetValue("UninstallString", "\"" + Path.Combine(directory, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            // Preserve whether the user enabled startup, only replace its location.
            if (runBefore.ContainsKey(_context.RunName))
                using (var key = Registry.CurrentUser.CreateSubKey(_context.RunKey))
                    key.SetValue(_context.RunName, "\"" + installed + "\" --tray");
            Directory.CreateDirectory(Path.GetDirectoryName(_context.Shortcut));
            CreateShortcut(_context.Shortcut, installed);
        }
        catch (Exception installError)
        {
            try
            {
            foreach (var file in Enumerable.Reverse(changed))
            {
                var target = Inside(directory, file);
                var saved = Inside(backup, file);
                if (File.Exists(saved)) File.Copy(saved, target, overwrite: true);
                else if (File.Exists(target)) File.Delete(target);
            }
            if (registrationStarted)
            {
                RestoreRegistry(_context.UninstallKey, registryBefore);
                using (var key = Registry.CurrentUser.CreateSubKey(_context.RunKey))
                {
                    if (runBefore.TryGetValue(_context.RunName, out var old)) key.SetValue(_context.RunName, old);
                    else key.DeleteValue(_context.RunName, throwOnMissingValue: false);
                }
                if (shortcutBefore != null) File.WriteAllBytes(_context.Shortcut, shortcutBefore);
                else if (File.Exists(_context.Shortcut)) File.Delete(_context.Shortcut);
            }
            }
            catch (Exception rollbackError)
            {
                cleanTemporaryFiles = false;
                throw new AggregateException("Setup failed and could not fully restore the old files. Recovery copies are in " + backup,
                    installError, rollbackError);
            }
            throw;
        }
        finally
        {
            // Only our unique staging directory; never the user's installation/data.
            try { if (cleanTemporaryFiles) Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    public void Uninstall(string directory)
    {
        directory = Path.GetFullPath(directory);
        using (var key = Registry.CurrentUser.OpenSubKey(_context.UninstallKey))
            if (!string.Equals(key?.GetValue("InstallLocation") as string, directory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("This folder is not the registered Mawaqit Adhan installation.");
        var manifest = new JavaScriptSerializer().Deserialize<InstallManifest>(File.ReadAllText(Inside(directory, ManifestName)));
        if (manifest?.Product != "MawaqitAdhan" || manifest.Files == null) throw new InvalidDataException("Invalid installation manifest.");
        foreach (var file in manifest.Files) Inside(directory, file);
        _context.StopRunning();
        foreach (var file in manifest.Files.Where(f => f != ManifestName)) File.Delete(Inside(directory, file));
        using (var key = Registry.CurrentUser.OpenSubKey(_context.RunKey, writable: true))
        {
            var path = CommandExecutable(key?.GetValue(_context.RunName) as string);
            if (string.Equals(path, Path.Combine(directory, "MawaqitAdhan.exe"), StringComparison.OrdinalIgnoreCase))
                key.DeleteValue(_context.RunName, throwOnMissingValue: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(_context.UninstallKey, throwOnMissingSubKey: false);
        if (File.Exists(_context.Shortcut)) File.Delete(_context.Shortcut);
        File.Delete(Inside(directory, ManifestName));
        // Remove only empty package directories, preserve custom files and AppData.
        foreach (var folder in manifest.Files.Select(p => Path.GetDirectoryName(Inside(directory, p)))
            .Distinct().OrderByDescending(p => p.Length))
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }

    private static Dictionary<string, object> ReadRegistry(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key == null ? new Dictionary<string, object>() : key.GetValueNames().ToDictionary(n => n, n => key.GetValue(n));
    }

    private static void RestoreRegistry(string path, Dictionary<string, object> values)
    {
        Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        if (values.Count == 0) return;
        using var key = Registry.CurrentUser.CreateSubKey(path);
        foreach (var pair in values) key.SetValue(pair.Key, pair.Value);
    }

    private static void CreateShortcut(string path, string executable)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object link = null;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            link = shortcut;
            shortcut.TargetPath = executable;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.Description = "Prayer times and adhan from your masjid";
            shortcut.Save();
        }
        finally
        {
            if (link != null) Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}

internal static class RunningApp
{
    public static IEnumerable<string> Paths()
    {
        foreach (var process in Process.GetProcessesByName("MawaqitAdhan"))
            using (process)
            {
                string path = null;
                try { if (process.SessionId == Process.GetCurrentProcess().SessionId) path = process.MainModule.FileName; } catch { }
                if (path != null) yield return path;
            }
    }

    public static void Stop()
    {
        var paths = Paths().Where(Installer.IsApp).Distinct().ToArray();
        if (paths.Length == 0) return;
        uint session;
        var key = Guid.NewGuid().ToString("N");
        var result = RmStartSession(out session, 0, key);
        if (result != 0) throw new IOException("Please exit Mawaqit Adhan from its tray menu before updating.");
        try
        {
            result = RmRegisterResources(session, (uint)paths.Length, paths, 0, IntPtr.Zero, 0, null);
            if (result == 0) result = RmShutdown(session, 0, IntPtr.Zero); // Graceful; never terminate forcibly.
            if (result != 0 || Paths().Any())
                throw new IOException("Please exit Mawaqit Adhan from its tray menu, then try again. No files have been replaced.");
        }
        finally { RmEndSession(session); }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint handle, int flags, string key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint handle, uint fileCount, string[] files, uint appCount, IntPtr apps, uint serviceCount, string[] services);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(uint handle, uint flags, IntPtr callback);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint handle);
}
