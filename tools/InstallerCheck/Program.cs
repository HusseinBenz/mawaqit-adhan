using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;
using MawaqitAdhan.Setup;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
        _checks++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: InstallerCheck <zip> <setup.exe> <previous-app.exe> [--running-app]"); return 1; }
        var temp = Path.Combine(Path.GetTempPath(), "MawaqitAdhan-InstallerCheck-" + Guid.NewGuid().ToString("N"));
        var registry = @"Software\MawaqitAdhan\SetupTests\" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temp);
        var context = new InstallContext
        {
            UninstallKey = registry + @"\Uninstall",
            RunKey = registry + @"\Run",
            Shortcut = Path.Combine(temp, "Start Menu", "Mawaqit Adhan.lnk"),
            DefaultDirectory = Path.Combine(temp, "New install"),
            StopRunning = () => { }
        };
        var engine = new Installer(context);
        void Install(string dir) { using var stream = File.OpenRead(args[0]); engine.Install(stream, dir, args[1]); }
        try
        {
            Check(engine.DetectDirectory() == context.DefaultDirectory, "fresh install defaults to the expected folder");
            Install(context.DefaultDirectory);
            Check(File.Exists(Path.Combine(context.DefaultDirectory, "MawaqitAdhan.exe")) && File.Exists(context.Shortcut), "fresh install creates app and Start menu shortcut");
            using (var key = Registry.CurrentUser.OpenSubKey(context.UninstallKey))
                Check((string)key.GetValue("DisplayVersion") == Installer.Version, "Windows uninstall registration records the version");
            using (var key = Registry.CurrentUser.OpenSubKey(context.RunKey))
                Check(key?.GetValue(context.RunName) == null, "fresh installation does not silently enable startup");
            Check(engine.DetectDirectory() == context.DefaultDirectory, "installed version is detected on repeat setup");
            File.WriteAllText(Path.Combine(context.DefaultDirectory, "my-custom-file.txt"), "keep");
            Install(context.DefaultDirectory);
            Check(File.ReadAllText(Path.Combine(context.DefaultDirectory, "my-custom-file.txt")) == "keep", "repeat installation preserves unknown files");
            engine.Uninstall(context.DefaultDirectory);
            Check(!File.Exists(Path.Combine(context.DefaultDirectory, "MawaqitAdhan.exe")) && File.Exists(Path.Combine(context.DefaultDirectory, "my-custom-file.txt")), "uninstall removes package files and retains custom files");
            Check(!File.Exists(context.Shortcut), "uninstall removes its shortcut");
            using (var key = Registry.CurrentUser.OpenSubKey(context.UninstallKey)) Check(key == null, "uninstall removes registration");

            var legacy = Path.Combine(temp, "Legacy portable with spaces");
            Directory.CreateDirectory(legacy);
            File.Copy(args[2], Path.Combine(legacy, "MawaqitAdhan.exe"));
            using (var key = Registry.CurrentUser.CreateSubKey(context.RunKey)) key.SetValue(context.RunName, "\"" + Path.Combine(legacy, "MawaqitAdhan.exe") + "\" --tray");
            Check(engine.DetectDirectory() == legacy, "portable installation is detected from the quoted startup path");
            var oldBytes = File.ReadAllBytes(Path.Combine(legacy, "MawaqitAdhan.exe"));
            context.AfterCopy = file => { if (file == "MawaqitAdhan.exe") throw new IOException("Simulated file-copy failure"); };
            try { Install(legacy); throw new Exception("Failure injection did not trigger"); }
            catch (IOException ex) when (ex.Message == "Simulated file-copy failure") { }
            Check(File.ReadAllBytes(Path.Combine(legacy, "MawaqitAdhan.exe")).SequenceEqual(oldBytes), "interrupted upgrade restores the exact old executable");
            Check(!File.Exists(Path.Combine(legacy, Installer.ManifestName)), "failed upgrade does not leave a new installation manifest");
            context.AfterCopy = null;
            var originalShortcut = context.Shortcut;
            var blocker = Path.Combine(temp, "shortcut-parent-is-a-file");
            File.WriteAllText(blocker, "block");
            context.Shortcut = Path.Combine(blocker, "app.lnk");
            try { Install(legacy); throw new Exception("Shortcut failure did not trigger"); }
            catch (IOException) { }
            Check(File.ReadAllBytes(Path.Combine(legacy, "MawaqitAdhan.exe")).SequenceEqual(oldBytes), "registration failure rolls the app files back");
            using (var key = Registry.CurrentUser.OpenSubKey(context.UninstallKey)) Check(key == null, "registration failure removes partial uninstall registration");
            using (var key = Registry.CurrentUser.OpenSubKey(context.RunKey)) Check((string)key.GetValue(context.RunName) == "\"" + Path.Combine(legacy, "MawaqitAdhan.exe") + "\" --tray", "registration failure restores the old startup command");
            context.Shortcut = originalShortcut;
            Install(legacy);
            Check(FileVersionInfo.GetVersionInfo(Path.Combine(legacy, "MawaqitAdhan.exe")).FileVersion == "1.0.0.2", "portable beta 1 is upgraded to beta 2 in place");
            using (var key = Registry.CurrentUser.OpenSubKey(context.RunKey))
                Check((string)key.GetValue(context.RunName) == "\"" + Path.Combine(legacy, "MawaqitAdhan.exe") + "\" --tray", "upgrade preserves enabled startup and updates its target");
            using (var badZip = new MemoryStream())
            {
                using (var zip = new ZipArchive(badZip, ZipArchiveMode.Create, true))
                using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("bad");
                badZip.Position = 0;
                try { engine.Install(badZip, legacy, args[1]); throw new Exception("Unsafe package accepted"); }
                catch (InvalidDataException) { Check(true, "package path traversal is rejected before changes"); }
            }
            engine.Uninstall(legacy);
            using (var key = Registry.CurrentUser.OpenSubKey(context.RunKey)) Check(key?.GetValue(context.RunName) == null, "uninstall removes only its own startup value");
            Check(Installer.CommandExecutable(@"C:\Folder With Spaces\MawaqitAdhan.exe --tray") == @"C:\Folder With Spaces\MawaqitAdhan.exe", "legacy unquoted startup paths are parsed");
            Check(!Directory.GetFiles(temp, "escape.txt", SearchOption.AllDirectories).Any(), "unsafe package creates no escaped file");

            if (args.Contains("--running-app"))
            {
                if (RunningApp.Paths().Any()) throw new Exception("Close the normal app before requesting the isolated process shutdown test.");
                var data = Path.Combine(temp, "test-app-data"); Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(data, "settings.json"), "{\"MosqueSlug\":\"omar-witten\",\"AdhanEnabled\":[false,false,false,false,false],\"ShowNotifications\":false}");
                var start = new ProcessStartInfo(args[2], "--tray") { UseShellExecute = false };
                start.EnvironmentVariables["MAWAQIT_ADHAN_DATA_DIR"] = data;
                using var app = Process.Start(start);
                try
                {
                    Thread.Sleep(1800);
                    Check(!app.HasExited, "previous portable application starts in an isolated data directory");
                    RunningApp.Stop();
                    Check(app.WaitForExit(10000), "Restart Manager closes the previous tray app gracefully");
                }
                finally { if (!app.HasExited) app.Kill(); }
            }
            Console.WriteLine($"ALL {_checks} INSTALLER CHECKS PASSED");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registry, false);
            // Unique test root only; retain output files for inspection.
            Console.WriteLine("Installer test files: " + temp);
        }
    }
}
