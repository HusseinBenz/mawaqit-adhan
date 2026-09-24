using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace MawaqitAdhan.Core;

/// <summary>
/// MP3 playback through the Windows MCI interface in winmm.dll - no third-party
/// audio library, no extra megabytes, and the volume of the adhan stays separate
/// from the system volume.
///
/// Every MCI call is handed to one long-lived STA thread: the MPEG device refuses
/// to load on an MTA thread, which is where timers and tasks would otherwise run.
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder buffer, int bufferSize, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    private static extern bool MciGetErrorString(int error, StringBuilder buffer, int bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(string path, StringBuilder shortPath, int bufferSize);

    private static readonly StaPump Pump = new();

    private readonly object _gate = new();
    private static int _aliasCounter;
    private string _alias;

    public string CurrentFile { get; private set; }

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                if (_alias == null) return false;
                var alias = _alias;
                var mode = Pump.Run(() => Query($"status {alias} mode"));
                return string.Equals(mode, "playing", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Starts playback. Returns null on success, or a human-readable error.</summary>
    public string Play(string path, int volumePercent)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "The adhan file could not be found.";

        lock (_gate)
        {
            CloseLocked();

            var alias = "adhan" + Interlocked.Increment(ref _aliasCounter);
            var volume = Numbers.Clamp(volumePercent, 0, 100) * 10; // MCI scale is 0-1000

            var result = Pump.Run(() =>
            {
                var error = Open(path, alias, out var openedWith);
                if (error != null) return (Error: error, Opened: (string)null);

                var code = MciSendString($"setaudio {alias} volume to {volume}", null, 0, IntPtr.Zero);
                if (code == 0) code = MciSendString($"play {alias}", null, 0, IntPtr.Zero);
                if (code == 0) return (Error: (string)null, Opened: openedWith);

                var text = Describe(code);
                MciSendString($"close {alias}", null, 0, IntPtr.Zero);
                return (Error: text, Opened: (string)null);
            });

            if (result.Error != null)
            {
                Log.Error("MCI playback failed: " + result.Error);
                return result.Error;
            }

            _alias = alias;
            CurrentFile = path;
            Log.Info($"Playing {Path.GetFileName(path)} at {volumePercent}% ({result.Opened})");
            return null;
        }
    }

    /// <summary>Adjusts the volume of the adhan itself while it plays. 0-100.</summary>
    public void SetVolume(int volumePercent)
    {
        lock (_gate)
        {
            if (_alias == null) return;
            var alias = _alias;
            var volume = Numbers.Clamp(volumePercent, 0, 100) * 10;
            Pump.Run(() => MciSendString($"setaudio {alias} volume to {volume}", null, 0, IntPtr.Zero));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_alias == null) return;
            var alias = _alias;
            Pump.Run(() => MciSendString($"stop {alias}", null, 0, IntPtr.Zero));
            CloseLocked();
        }
    }

    public void Dispose() => Stop();

    // ---- internals -----------------------------------------------------

    /// <summary>Runs on the STA pump.</summary>
    private static string Open(string path, string alias, out string openedWith)
    {
        // "mpegvideo" is the MCI MPEG device and handles .mp3 everywhere; the other
        // two forms are there for odd setups and exotic paths.
        var attempts = new List<(string Command, string Label)>
        {
            ($"open \"{path}\" type mpegvideo alias {alias}", "mpegvideo"),
            ($"open \"{path}\" alias {alias}", "by extension")
        };

        var shortPath = TryGetShortPath(path);
        if (shortPath != null && !string.Equals(shortPath, path, StringComparison.OrdinalIgnoreCase))
            attempts.Add(($"open {shortPath} type mpegvideo alias {alias}", "short path"));

        var lastError = "Windows could not open the audio file.";

        foreach (var (command, label) in attempts)
        {
            var code = MciSendString(command, null, 0, IntPtr.Zero);
            if (code == 0)
            {
                openedWith = label;
                return null;
            }
            lastError = Describe(code);
            Log.Warn($"MCI open failed ({label}): {lastError}");
        }

        openedWith = null;
        return lastError;
    }

    private void CloseLocked()
    {
        if (_alias == null) return;
        var alias = _alias;
        Pump.Run(() => MciSendString($"close {alias}", null, 0, IntPtr.Zero));
        _alias = null;
        CurrentFile = null;
    }

    private static string Query(string command)
    {
        var buffer = new StringBuilder(128);
        return MciSendString(command, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : null;
    }

    private static string Describe(int code)
    {
        var buffer = new StringBuilder(256);
        return MciGetErrorString(code, buffer, buffer.Capacity)
            ? buffer.ToString()
            : $"MCI error {code}.";
    }

    private static string TryGetShortPath(string path)
    {
        try
        {
            var buffer = new StringBuilder(512);
            var n = GetShortPathName(path, buffer, buffer.Capacity);
            return n > 0 && n < buffer.Capacity ? buffer.ToString() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// A single background STA thread that runs whatever work is handed to it.
    /// MCI's MPEG device fails to load on MTA threads, so timers, tasks and the
    /// playback sequence all funnel their commands through here.
    /// </summary>
    private sealed class StaPump
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;

        public StaPump()
        {
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "MawaqitAdhan audio"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        private void Loop()
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { Log.Error("Audio thread work failed", ex); }
            }
        }

        public T Run<T>(Func<T> work)
        {
            if (Thread.CurrentThread == _thread) return work();

            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try { done.SetResult(work()); }
                catch (Exception ex) { done.SetException(ex); }
            });
            // Never report success while a timed-out command can still start later.
            return done.Task.GetAwaiter().GetResult();
        }

        public void Run(Action work) => Run<object>(() =>
        {
            work();
            return null;
        });
    }
}
