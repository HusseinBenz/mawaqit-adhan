using System.Runtime.CompilerServices;
using Windows.Media.Control;

namespace MawaqitAdhan.Core;

internal interface IMediaSession
{
    bool IsPlaying { get; }
    bool IsPaused { get; }
    Task<bool> PauseAsync();
    Task<bool> PlayAsync();
}

// Keep the exact sessions we paused. Never send a global play/pause key.
internal sealed class MediaSessions
{
    private readonly List<IMediaSession> _paused = new();

    public async Task<bool> PauseAsync(IEnumerable<IMediaSession> sessions)
    {
        foreach (var session in sessions)
        {
            try
            {
                if (session.IsPlaying && await session.PauseAsync().ConfigureAwait(false))
                    _paused.Add(session);
            }
            catch (Exception ex) { Log.Warn("Could not pause media session: " + ex.Message); }
        }
        return _paused.Count > 0;
    }

    public async Task ResumeAsync()
    {
        try
        {
            foreach (var session in _paused)
            {
                try
                {
                    if (session.IsPaused) await session.PlayAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { Log.Warn("Could not resume media session: " + ex.Message); }
            }
        }
        finally { _paused.Clear(); }
    }

    public async Task<bool> PauseWindowsAsync()
    {
        try { return await PauseWindowsCoreAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            // Older Windows and unsupported players are left alone, never toggled.
            Log.Warn("Session-specific media control unavailable: " + ex.Message);
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<bool> PauseWindowsCoreAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
            .AsTask(timeout.Token).ConfigureAwait(false);
        return await PauseAsync(manager.GetSessions().Select(s => (IMediaSession)new WindowsSession(s)).ToArray())
            .ConfigureAwait(false);
    }

    private sealed class WindowsSession : IMediaSession
    {
        private readonly GlobalSystemMediaTransportControlsSession _session;
        public WindowsSession(GlobalSystemMediaTransportControlsSession session) => _session = session;
        public bool IsPlaying => _session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        public bool IsPaused => _session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
        public async Task<bool> PauseAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            return await _session.TryPauseAsync().AsTask(timeout.Token).ConfigureAwait(false);
        }
        public async Task<bool> PlayAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            return await _session.TryPlayAsync().AsTask(timeout.Token).ConfigureAwait(false);
        }
    }
}
