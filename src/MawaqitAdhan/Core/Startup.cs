using Microsoft.Win32;

namespace MawaqitAdhan.Core;

/// <summary>Registers the app in the per-user "Run" key so it can start with Windows.</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MawaqitAdhan";

    public static string ExecutablePath =>
        System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName ?? Path.Combine(AppPaths.AppDir, "MawaqitAdhan.exe");

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.IndexOf("MawaqitAdhan", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the startup entry: " + ex.Message);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null) return false;

            if (enabled) key.SetValue(ValueName, $"\"{ExecutablePath}\" --tray");
            else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, throwOnMissingValue: false);

            Log.Info($"Start with Windows: {enabled}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not change the startup entry", ex);
            return false;
        }
    }
}
