using System.Diagnostics;

namespace MawaqitAdhan.Core;

public enum PlaybackPhase { Idle, FadingOut, PausingOthers, Playing, WaitingToResume, FadingIn }

public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }
    string Play(string path, int volumePercent);
    void Stop();
    void SetVolume(int percent);
}

internal interface IPlaybackEnvironment
{
    float? GetVolume();
    void SetVolume(float value);
    bool IsMuted();
    bool OtherAudioPlaying();
    Task<bool> PauseMediaAsync();
    Task ResumeMediaAsync();
}

internal sealed class WindowsPlaybackEnvironment : IPlaybackEnvironment
{
    public float? GetVolume() => SystemAudio.GetMasterVolume();
    public void SetVolume(float value) => SystemAudio.SetMasterVolume(value);
    public bool IsMuted() => SystemAudio.IsMuted();
    public bool OtherAudioPlaying() => SystemAudio.IsAnyAudioPlaying();
    private readonly MediaSessions _media = new();
    public Task<bool> PauseMediaAsync() => _media.PauseWindowsAsync();
    public Task ResumeMediaAsync() => _media.ResumeAsync();
}

/// <summary>Owns one cancellable playback sequence, including restoration on Stop and exit.</summary>
public sealed class AdhanPlayback : IDisposable
{
    private readonly IAudioPlayer _player;
    private readonly IPlaybackEnvironment _audio;
    private readonly object _gate = new();
    private CancellationTokenSource _cts;
    private Task _running;
    private bool _disposed;
    private volatile PlaybackPhase _phase;
    public PlaybackPhase Phase => _phase;
    public string NowPlaying { get; private set; }
    public string LastError { get; private set; }
    public bool IsBusy { get { lock (_gate) return _running != null && !_running.IsCompleted; } }
    public bool IsSounding => Phase == PlaybackPhase.Playing;
    public event EventHandler PhaseChanged;

    public AdhanPlayback() : this(new AudioPlayer(), new WindowsPlaybackEnvironment()) { }
    internal AdhanPlayback(IAudioPlayer player, IPlaybackEnvironment audio) { _player = player; _audio = audio; }

    public bool Start(string audioFile, AppSettings settings, string label)
    {
        lock (_gate)
        {
            if (_disposed || IsBusy) return false;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var snapshot = new AppSettings
            {
                DuckOtherAudio = settings.DuckOtherAudio,
                OnlyDuckWhenAudioPlaying = settings.OnlyDuckWhenAudioPlaying,
                PauseOtherAudio = settings.PauseOtherAudio,
                FadeOutSeconds = Numbers.Clamp(settings.FadeOutSeconds, 0, 30),
                FadeInSeconds = Numbers.Clamp(settings.FadeInSeconds, 0, 30),
                ResumeDelaySeconds = Numbers.Clamp(settings.ResumeDelaySeconds, 0, 120),
                Volume = Numbers.Clamp(settings.Volume, 0, 100)
            };
            LastError = null;
            NowPlaying = label;
            _running = Task.Run(() => RunAsync(audioFile, snapshot, token));
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _cts?.Cancel();
            _player.Stop();
        }
    }

    public void SetVolume(int percent) => _player.SetVolume(percent);

    public void Dispose()
    {
        Task running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _player.Stop();
            running = _running;
        }
        // Restoration completes before exit; cancellation skips the resume delay and fade.
        try { running?.GetAwaiter().GetResult(); }
        finally { _cts?.Dispose(); _player.Dispose(); }
    }

    private async Task RunAsync(string file, AppSettings s, CancellationToken ct)
    {
        var paused = false;
        float? restore = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (s.DuckOtherAudio)
            {
                var original = _audio.GetVolume();
                if (original > 0.001f && !_audio.IsMuted() &&
                    (!s.OnlyDuckWhenAudioPlaying || _audio.OtherAudioPlaying()))
                {
                    restore = original;
                    SetPhase(PlaybackPhase.FadingOut);
                    await FadeAsync(original.Value, 0, s.FadeOutSeconds, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (s.PauseOtherAudio)
                    {
                        SetPhase(PlaybackPhase.PausingOthers);
                        paused = await _audio.PauseMediaAsync().ConfigureAwait(false);
                        await Task.Delay(400, ct).ConfigureAwait(false);
                    }
                    _audio.SetVolume(original.Value);
                    restore = null;
                }
            }

            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                SetPhase(PlaybackPhase.Playing);
                var error = _player.Play(file, s.Volume);
                if (error != null) throw new InvalidOperationException(error);
            }
            await Task.Delay(350, ct).ConfigureAwait(false);
            var elapsed = Stopwatch.StartNew();
            while (_player.IsPlaying && elapsed.Elapsed < TimeSpan.FromMinutes(12))
                await Task.Delay(200, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LastError = ex.Message; Log.Error("Adhan playback failed", ex); }
        finally
        {
            try
            {
                _player.Stop();
                if (restore.HasValue) { _audio.SetVolume(restore.Value); restore = null; }
                if (paused)
                {
                    if (!ct.IsCancellationRequested && LastError == null)
                    {
                        SetPhase(PlaybackPhase.WaitingToResume);
                        try { await Task.Delay(TimeSpan.FromSeconds(s.ResumeDelaySeconds), ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                    }
                    // Session-specific resume preserves players that already resumed.
                    if (!_audio.OtherAudioPlaying())
                    {
                        SetPhase(PlaybackPhase.FadingIn);
                        restore = _audio.GetVolume();
                        if (restore.HasValue && !ct.IsCancellationRequested) _audio.SetVolume(0);
                        await _audio.ResumeMediaAsync().ConfigureAwait(false);
                        if (restore.HasValue && !ct.IsCancellationRequested)
                        {
                            try { await FadeAsync(0, restore.Value, s.FadeInSeconds, ct).ConfigureAwait(false); }
                            catch (OperationCanceledException) { }
                        }
                    }
                    else await _audio.ResumeMediaAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) { Log.Error("Could not restore the other audio", ex); }
            finally
            {
                if (restore.HasValue) _audio.SetVolume(restore.Value);
                NowPlaying = null;
                SetPhase(PlaybackPhase.Idle);
            }
        }
    }

    private async Task FadeAsync(float from, float to, double seconds, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        while (seconds > 0 && watch.Elapsed.TotalSeconds < seconds)
        {
            ct.ThrowIfCancellationRequested();
            _audio.SetVolume(from + (to - from) * (float)(watch.Elapsed.TotalSeconds / seconds));
            await Task.Delay(40, ct).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        _audio.SetVolume(to);
    }

    private void SetPhase(PlaybackPhase phase)
    {
        _phase = phase;
        try { PhaseChanged?.Invoke(this, EventArgs.Empty); }
        catch (InvalidOperationException) { /* window closed */ }
    }
}
