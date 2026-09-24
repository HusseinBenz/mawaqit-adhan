using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MawaqitAdhan.Core;

/// <summary>
/// Master volume of the default playback device, plus a "is anything actually
/// making noise right now" probe, through the Core Audio (WASAPI) COM API.
/// Every call is best-effort: audio devices come and go, and none of this is
/// worth crashing over.
/// </summary>
public static class SystemAudio
{
    private static readonly Guid EventContext = Guid.NewGuid();

    public static float? GetMasterVolume()
    {
        try
        {
            var vol = OpenEndpointVolume(out var device);
            try
            {
                if (vol == null) return null;
                return vol.GetMasterVolumeLevelScalar(out var level) == 0 ? level : (float?)null;
            }
            finally { Release(vol); Release(device); }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the system volume: " + ex.Message);
            return null;
        }
    }

    public static bool SetMasterVolume(float scalar)
    {
        try
        {
            var vol = OpenEndpointVolume(out var device);
            try
            {
                if (vol == null) return false;
                var ctx = EventContext;
                return vol.SetMasterVolumeLevelScalar(Numbers.Clamp(scalar, 0f, 1f), ref ctx) == 0;
            }
            finally { Release(vol); Release(device); }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not set the system volume: " + ex.Message);
            return false;
        }
    }

    public static bool IsMuted()
    {
        try
        {
            var vol = OpenEndpointVolume(out var device);
            try
            {
                if (vol == null) return false;
                return vol.GetMute(out var muted) == 0 && muted;
            }
            finally { Release(vol); Release(device); }
        }
        catch { return false; }
    }

    /// <summary>
    /// True if some other program is producing sound right now. Sampled a few
    /// times, because a paused player still owns an "active" session and a
    /// playing one can momentarily sit at silence.
    /// </summary>
    public static bool IsAnyAudioPlaying(int samples = 5, int sampleDelayMs = 60)
    {
        var ownPid = Process.GetCurrentProcess().Id;

        try
        {
            for (var attempt = 0; attempt < Math.Max(1, samples); attempt++)
            {
                if (attempt > 0) Thread.Sleep(sampleDelayMs);
                if (ProbePeaks(ownPid)) return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not inspect audio sessions: " + ex.Message);
            return false;
        }

        return false;
    }

    internal static bool IsOwnAudioPlaying() => ProbePeaks(Process.GetCurrentProcess().Id, ownOnly: true);

    private static bool ProbePeaks(int ownPid, bool ownOnly = false)
    {
        IMMDevice device = null;
        IAudioSessionManager2 manager = null;
        IAudioSessionEnumerator sessions = null;

        try
        {
            device = DefaultRenderDevice();
            if (device == null) return false;

            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var raw) != 0 || raw == null) return false;
            manager = (IAudioSessionManager2)raw;

            if (manager.GetSessionEnumerator(out sessions) != 0 || sessions == null) return false;
            if (sessions.GetCount(out var count) != 0) return false;

            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl session = null;
                try
                {
                    if (sessions.GetSession(i, out session) != 0 || session == null) continue;
                    if (session.GetState(out var state) != 0 || state != AudioSessionState.Active) continue;

                    if (session is IAudioSessionControl2 s2)
                    {
                        if (s2.IsSystemSoundsSession() == 0) continue;           // beeps and notification dings
                        if (s2.GetProcessId(out var pid) != 0 || (ownOnly ? pid != ownPid : pid == ownPid)) continue;
                    }

                    if (session is IAudioMeterInformation meter &&
                        meter.GetPeakValue(out var peak) == 0 && peak > 0.0008f)
                        return true;
                }
                catch
                {
                    // a session can disappear mid-enumeration
                }
                finally { Release(session); }
            }
        }
        finally
        {
            Release(sessions);
            Release(manager);
            Release(device);
        }

        return false;
    }

    // ---- plumbing ------------------------------------------------------

    private const int ClsCtxAll = 23;

    private static IMMDevice DefaultRenderDevice()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            return enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) == 0
                ? device
                : null;
        }
        finally { Release(enumerator); }
    }

    private static IAudioEndpointVolume OpenEndpointVolume(out IMMDevice device)
    {
        device = DefaultRenderDevice();
        if (device == null) return null;

        var iid = typeof(IAudioEndpointVolume).GUID;
        return device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var raw) == 0 && raw != null
            ? (IAudioEndpointVolume)raw
            : null;
    }

    private static void Release(object com)
    {
        try
        {
            if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }
        catch
        {
            // ignored
        }
    }

    // ---- COM definitions ------------------------------------------------

    private enum EDataFlow { Render, Capture, All }

    private enum ERole { Console, Multimedia, Communications }

    private enum AudioSessionState { Inactive, Active, Expired }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr session);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out AudioSessionState state);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out AudioSessionState state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out int pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}

/// <summary>Sends the keyboard media keys, so whatever is playing pauses and resumes itself.</summary>
public static class MediaKeys
{
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);

    public static void TogglePlayPause()
    {
        try
        {
            keybd_event(VkMediaPlayPause, 0, KeyEventExtendedKey, UIntPtr.Zero);
            keybd_event(VkMediaPlayPause, 0, KeyEventExtendedKey | KeyEventKeyUp, UIntPtr.Zero);
            Log.Info("Sent media play/pause");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not send the media key: " + ex.Message);
        }
    }

    /// <summary>Only used for diagnostics in the log.</summary>
    public static string DescribeForeground()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return "?"; }
    }
}
