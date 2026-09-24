using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MawaqitAdhan.Core;
using Timer = System.Windows.Forms.Timer;

namespace MawaqitAdhan.Ui;

public sealed class MainForm : ThemedForm
{
    private readonly AppSettings _settings;
    private readonly AdhanPlayback _playback = new();
    private readonly Timer _tick = new() { Interval = 1000 };

    private Timetable _timetable;
    private CancellationTokenSource _fetch;
    private DateTime _lastFetchAttemptUtc = DateTime.MinValue;
    private string _statusText = "";
    private bool _exiting;
    private string _listKey;
    private Timetable _scheduleTable;
    private DateTime _scheduleDate;
    private List<PrayerEvent> _schedule = new();
    private bool _disposed;

    private readonly HashSet<string> _preNotified = new();
    private readonly HashSet<string> _iqamaNotified = new();

    // ---- controls ----
    private NotifyIcon _tray;
    private HeaderPanel _header;
    private CountdownCard _card;
    private PrayerListView _list;
    private Label _dateLabel;
    private Label _status;
    private FlatButton _testButton;
    private FlatButton _stopButton;

    private static readonly int WmShowApp = RegisterWindowMessage("MawaqitAdhan.ShowWindow");

    public MainForm(AppSettings settings, bool startHidden)
    {
        _settings = settings;
        BuildUi();

        _timetable = _settings.HasMosque ? Timetable.LoadCache(_settings.MosqueSlug) : null;
        if (_timetable != null) ShowMosque(_timetable);

        _playback.PhaseChanged += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated && !_exiting)
                BeginInvoke((Action)(() =>
                {
                    if (_disposed) return;
                    UpdateButtons();
                    if (_playback.LastError != null) SetStatus("Audio failed: " + _playback.LastError);
                    UpdateDisplay();
                }));
        };
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        Shown += (_, _) => OnLoaded(startHidden);
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray) HideToTray();
        };
    }

    // ---- layout --------------------------------------------------------

    private void BuildUi()
    {
        Text = "Mawaqit Adhan";
        Icon = Theme.AppIcon;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = Dpi.Sz(424, 592);
        DoubleBuffered = true;
        KeyPreview = true;

        _header = new HeaderPanel { Bounds = new Rectangle(0, 0, ClientSize.Width, Dpi.S(76)) };
        _header.RefreshClicked += (_, _) => _ = RefreshAsync(force: true);
        _header.SettingsClicked += (_, _) => OpenSettings();
        Controls.Add(_header);

        _card = new CountdownCard { Bounds = Dpi.R(12, 86, 400, 122) };
        Controls.Add(_card);

        _list = new PrayerListView { Bounds = Dpi.R(12, 218, 400, 262) };
        Controls.Add(_list);

        _dateLabel = new Label
        {
            Bounds = Dpi.R(12, 486, 400, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            BackColor = Color.Transparent
        };
        Controls.Add(_dateLabel);

        _testButton = new FlatButton { Text = "Test adhan", Bounds = Dpi.R(12, 512, 124, 32) };
        _testButton.Click += (_, _) => TestAdhan();
        Controls.Add(_testButton);

        _stopButton = new FlatButton { Text = "Stop", Bounds = Dpi.R(144, 512, 104, 32), Enabled = false };
        _stopButton.Click += (_, _) => _playback.Stop();
        Controls.Add(_stopButton);

        var change = new FlatButton { Text = "Change masjid", Bounds = Dpi.R(256, 512, 156, 32) };
        change.Click += (_, _) => ChooseMosque();
        Controls.Add(change);

        _status = new Label
        {
            Bounds = Dpi.R(12, 552, 400, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Faint,
            Font = Theme.Small,
            BackColor = Color.Transparent
        };
        Controls.Add(_status);

        BuildTray();
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Test adhan", null, (_, _) => TestAdhan());
        menu.Items.Add("Stop adhan", null, (_, _) => _playback.Stop());
        menu.Items.Add("Refresh times", null, (_, _) => _ = RefreshAsync(force: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Change masjid...", null, (_, _) => ChooseMosque());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = Theme.AppIcon,
            Visible = true,
            Text = "Mawaqit Adhan",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    // ---- lifecycle -----------------------------------------------------

    private async void OnLoaded(bool startHidden)
    {
        if (startHidden) HideToTray();

        if (!_settings.HasMosque)
        {
            ChooseMosque();
            return;
        }

        UpdateDisplay();
        await RefreshAsync(force: _timetable == null);
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (_exiting || e.CloseReason != CloseReason.UserClosing || !_settings.CloseToTray) return;
        e.Cancel = true;
        HideToTray();
    }

    private void ExitApp()
    {
        _exiting = true;
        _tick.Stop();
        Application.Exit();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        WindowState = FormWindowState.Normal;
    }

    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void WndProc(ref Message m)
    {
        // a second copy of the app was started - surface this one instead
        if (m.Msg == WmShowApp && WmShowApp != 0) ShowFromTray();
        base.WndProc(ref m);
    }

    // ---- the heartbeat -------------------------------------------------

    private void OnTick()
    {
        try
        {
            if (_exiting || _disposed) return;
            EnsureSchedule();
            if (Visible) UpdateDisplay();
            FireDuePrayers();
            if (Visible) UpdateButtons();
            MaybeAutoRefresh();
        }
        catch (Exception ex)
        {
            Log.Error("Tick failed", ex);
        }
    }

    private void UpdateDisplay()
    {
        var now = DateTimeOffset.Now;

        if (_timetable == null)
        {
            _card.Set("NEXT PRAYER", "--", "--:--:--", _settings.HasMosque
                ? "Loading the timetable..."
                : "Choose a masjid to begin");
            _dateLabel.Text = now.ToString("dddd d MMMM yyyy");
            _status.Text = _statusText;
            return;
        }

        var today = _timetable.MosqueToday();
        EnsureSchedule();
        var day = _schedule.Where(p => p.MosqueDate == today).ToList();
        var next = _schedule.FirstOrDefault(p => p.Adhan > now);
        var current = _schedule.LastOrDefault(p => p.Adhan <= now);

        var key = $"{today:yyyy-MM-dd}|{next?.Prayer}|{current?.Prayer}|{string.Join("", _settings.AdhanEnabled)}";
        if (key != _listKey)
        {
            _listKey = key;
            _list.SetDay(day, next?.MosqueDate == today ? next.Prayer : (Prayer?)null,
                current?.MosqueDate == today ? current.Prayer : (Prayer?)null, _settings.AdhanEnabled);
        }

        if (_playback.IsBusy)
        {
            var (title, sub) = DescribePlayback();
            _card.Set(title, _playback.NowPlaying ?? "Adhan", "", sub);
        }
        else if (next != null)
        {
            var left = next.Adhan - now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;

            var sub = $"{next.Name} at {next.AdhanClock}";
            if (TimeZoneInfo.ConvertTime(next.Adhan, _timetable.ResolvedTimeZone).Date > today)
                sub = $"Tomorrow: {next.Name} at {next.AdhanClock}";
            if (next.IqamaClock != null) sub += $"   ·   iqama {next.IqamaClock}";
            if (_timetable.IsRemoteTimezone)
                sub += $"   ·   {TimeZoneInfo.ConvertTime(next.Adhan, TimeZoneInfo.Local):HH:mm} your time";

            _card.Set("NEXT PRAYER", next.Name,
                $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}", sub);
        }
        else
        {
            _card.Set("NEXT PRAYER", "--", "--:--:--", "No times available for today");
        }

        var hijri = HijriDate.Format(today, _timetable.HijriAdjustment);
        _dateLabel.Text = today.ToString("dddd d MMMM yyyy") + (hijri == null ? "" : "   ·   " + hijri);

        if (today.DayOfWeek == DayOfWeek.Friday && !string.IsNullOrWhiteSpace(_timetable.Jumua))
            _dateLabel.Text += $"   ·   Jumu'ah {_timetable.Jumua}";

        _status.Text = _statusText;
    }

    private (string Title, string Sub) DescribePlayback() => _playback.Phase switch
    {
        PlaybackPhase.FadingOut => ("FADING OUT OTHER AUDIO", "Making room for the adhan"),
        PlaybackPhase.PausingOthers => ("PAUSING OTHER AUDIO", "Pressing play/pause"),
        PlaybackPhase.Playing => ("ADHAN", "Press Stop to silence it"),
        PlaybackPhase.WaitingToResume => ("ADHAN FINISHED",
            $"Resuming your audio in {_settings.ResumeDelaySeconds:0.#}s"),
        PlaybackPhase.FadingIn => ("RESUMING YOUR AUDIO", "Fading back in"),
        _ => ("NEXT PRAYER", "")
    };

    private void EnsureSchedule()
    {
        if (_timetable == null) { _schedule.Clear(); _scheduleTable = null; return; }
        var today = _timetable.MosqueToday();
        if (_scheduleTable == _timetable && _scheduleDate == today) return;
        _schedule = Enumerable.Range(-1, 9).SelectMany(i => PrayerSchedule.Day(_timetable, today.AddDays(i)))
            .OrderBy(p => p.Adhan).ToList();
        _scheduleTable = _timetable;
        _scheduleDate = today;
        var keys = new HashSet<string>(_schedule.Select(p => p.Key));
        _preNotified.IntersectWith(keys);
        _iqamaNotified.IntersectWith(keys);
    }

    private void FireDuePrayers()
    {
        if (_timetable == null) return;

        var now = DateTimeOffset.Now;

        // a three minute tail means a short sleep or a busy moment never costs an adhan
        foreach (var p in _schedule.Where(p => p.Adhan >= now.AddMinutes(-3) && p.Adhan <= now))
        {
            if (!p.CallsAdhan || _settings.FiredKeys.Contains(p.Key)) continue;
            if (_playback.IsBusy && p.AdhanSlot >= 0 && _settings.AdhanEnabled[p.AdhanSlot]) continue;

            _settings.FiredKeys.Add(p.Key);
            _settings.Save();

            if (p.AdhanSlot < 0 || !_settings.AdhanEnabled[p.AdhanSlot])
            {
                Log.Info($"{p.Name} at {p.AdhanClock} - adhan switched off, skipping");
                continue;
            }

            Log.Info($"{p.Name} at {p.AdhanClock} - playing adhan");
            Notify($"{p.Name}  ·  {p.AdhanClock}",
                _timetable.Name + (p.IqamaClock != null ? $"\nIqama at {p.IqamaClock}" : ""));
            PlayAdhan(p);
        }

        NotifyUpcoming(now);
    }

    private void NotifyUpcoming(DateTimeOffset now)
    {
        if (!_settings.ShowNotifications) return;

        if (_settings.NotifyBeforeAdhan)
        {
            var next = _schedule.FirstOrDefault(p => p.CallsAdhan && p.Adhan > now);
            if (next != null && !_preNotified.Contains(next.Key))
            {
                var left = next.Adhan - now;
                if (left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(_settings.NotifyBeforeMinutes))
                {
                    _preNotified.Add(next.Key);
                    Notify($"{next.Name} in {Math.Max(1, (int)Math.Round(left.TotalMinutes))} min",
                        $"Adhan at {next.AdhanClock}");
                }
            }
        }

        if (!_settings.NotifyAtIqama) return;

        foreach (var p in _schedule)
        {
            if (p.Iqama == null || _iqamaNotified.Contains(p.Key)) continue;
            if (p.Iqama.Value <= now && now - p.Iqama.Value < TimeSpan.FromMinutes(1))
            {
                _iqamaNotified.Add(p.Key);
                Notify($"Iqama  ·  {p.Name}", $"{p.IqamaClock} at {_timetable.Name}");
            }
        }
    }

    // ---- playback ------------------------------------------------------

    private void PlayAdhan(PrayerEvent p)
    {
        var isFajr = p?.Prayer == Prayer.Fajr;
        var voice = isFajr
            ? _settings.FajrAdhanVoice ?? _settings.AdhanVoice
            : _settings.AdhanVoice;

        var file = AppPaths.ResolveAudio(voice)
                   ?? AppPaths.ResolveAudio(_settings.AdhanVoice)
                   ?? AudioLibrary.AnyAvailableFile();

        if (file == null)
        {
            _statusText = "No adhan audio found - add one in Settings.";
            Log.Error("No adhan audio available to play");
            Notify("No adhan audio", "Open Settings and download an adhan voice.");
            return;
        }

        if (!_playback.Start(file, _settings, p?.Name ?? "Adhan"))
            Log.Warn("An adhan is already playing, ignoring the new one");

        UpdateButtons();
    }

    private void TestAdhan()
    {
        if (_playback.IsBusy) { _playback.Stop(); return; }
        PlayAdhan(null);
    }

    private void UpdateButtons()
    {
        _stopButton.Enabled = _playback.IsBusy;
        _testButton.Text = _playback.IsBusy ? "Playing..." : "Test adhan";
    }

    private void Notify(string title, string text)
    {
        if (!_settings.ShowNotifications || _tray == null) return;
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = text;
            _tray.ShowBalloonTip(8000);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not show the notification: " + ex.Message);
        }
    }

    // ---- masjid data ---------------------------------------------------

    private void ChooseMosque()
    {
        ShowFromTray();

        using var picker = new MosquePickerForm(_settings.MosqueSlug);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected == null)
        {
            if (!_settings.HasMosque && _timetable == null) _statusText = "No masjid chosen yet.";
            return;
        }

        _settings.MosqueSlug = picker.Selected.Slug;
        _settings.MosqueName = picker.Selected.Title;
        _settings.MosqueLocation = picker.Selected.Localisation;
        _settings.LastFetchUtc = null;
        _settings.Save();

        _timetable = Timetable.LoadCache(_settings.MosqueSlug);
        if (_timetable != null) ShowMosque(_timetable);
        else _header.SetMosque(_settings.MosqueName, _settings.MosqueLocation);
        _listKey = null;
        _preNotified.Clear();
        _iqamaNotified.Clear();
        _ = RefreshAsync(force: true);
    }

    private void OpenSettings()
    {
        ShowFromTray();
        using var dialog = new SettingsForm(_settings, _playback);
        dialog.ShowDialog(this);
        _settings.Save();
        _listKey = null;
        UpdateDisplay();
    }

    private void MaybeAutoRefresh()
    {
        if (!_settings.HasMosque || _disposed || _exiting) return;
        if (DateTime.UtcNow - _lastFetchAttemptUtc < TimeSpan.FromMinutes(20)) return;

        var stale = _settings.LastFetchUtc == null ||
                    DateTime.UtcNow - _settings.LastFetchUtc.Value > TimeSpan.FromHours(12);
        var missingToday = _timetable == null || _timetable.TimesFor(_timetable.MosqueToday()) == null;

        if (stale || missingToday) _ = RefreshAsync(force: false);
    }

    private async Task RefreshAsync(bool force)
    {
        if (!_settings.HasMosque || _disposed || _exiting) return;

        _lastFetchAttemptUtc = DateTime.UtcNow;
        _fetch?.Cancel();
        _fetch?.Dispose();
        _fetch = new CancellationTokenSource();
        var token = _fetch.Token;

        SetStatus("Updating times from mawaqit...");

        try
        {
            var fetched = await MawaqitClient.FetchTimetableAsync(_settings.MosqueSlug, token);
            if (token.IsCancellationRequested || _disposed || _exiting) return;

            fetched.Location = _settings.MosqueLocation;
            fetched.SaveCache();

            _timetable = fetched;
            _listKey = null;
            _settings.MosqueName = fetched.Name;
            _settings.LastFetchUtc = DateTime.UtcNow;
            _settings.Save();

            ShowMosque(fetched);
            SetStatus($"Updated {DateTime.Now:HH:mm}" +
                      (fetched.IsRemoteTimezone ? $"   ·   masjid time zone {fetched.Timezone}" : ""));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Replaced by a newer refresh or closed; real network timeouts reach the error UI.
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || _disposed || _exiting) return;
            Log.Error("Could not update the timetable", ex);

            if (_timetable == null && force)
            {
                SetStatus("Could not reach mawaqit. " + ex.Message);
                MessageBox.Show(this, ex.Message + "\n\nThe app will keep trying in the background.",
                    "Mawaqit Adhan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                var cachedOn = _settings.LastFetchUtc?.ToLocalTime().ToString("d MMM HH:mm") ?? "earlier";
                SetStatus($"Offline - using the timetable saved on {cachedOn}");
            }
        }
        finally
        {
            if (!token.IsCancellationRequested && !_disposed && !_exiting) UpdateDisplay();
        }
    }

    private void ShowMosque(Timetable t)
    {
        _header.SetMosque(t.Name, string.IsNullOrWhiteSpace(t.Location)
            ? BuildLocationLine(t)
            : t.Location);
        _tray.Text = Truncate($"Mawaqit Adhan - {t.Name}", 62);
    }

    private static string BuildLocationLine(Timetable t)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.CountryCode)) bits.Add(t.CountryCode.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(t.Timezone)) bits.Add(t.Timezone);
        return string.Join("   ·   ", bits);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    private void SetStatus(string text)
    {
        _statusText = text;
        if (_status != null) _status.Text = text;
    }

    // ---- window chrome -------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _exiting = true;
            _fetch?.Cancel();
            _fetch?.Dispose();
            _settings.Save();
            _tick?.Dispose();
            _playback?.Dispose();
            _tray?.ContextMenuStrip?.Dispose();
            _tray?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---- painted pieces -------------------------------------------------

    private sealed class HeaderPanel : Control
    {
        private readonly FlatButton _refresh;
        private readonly FlatButton _settings;
        private string _name = "No masjid selected";
        private string _place = "Pick one from mawaqit to get started";

        public event EventHandler RefreshClicked;
        public event EventHandler SettingsClicked;

        public HeaderPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;

            _settings = new FlatButton { Text = "⚙", AccessibleName = "Settings", Font = new Font("Segoe UI Symbol", 10f), Size = Dpi.Sz(32, 32) };
            _settings.Click += (s, e) => SettingsClicked?.Invoke(s, e);

            _refresh = new FlatButton { Text = "↻", AccessibleName = "Refresh times", Font = new Font("Segoe UI Symbol", 12f), Size = Dpi.Sz(32, 32) };
            _refresh.Click += (s, e) => RefreshClicked?.Invoke(s, e);

            Controls.Add(_settings);
            Controls.Add(_refresh);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _settings.Location = new Point(Width - Dpi.S(44), Dpi.S(22));
            _refresh.Location = new Point(Width - Dpi.S(80), Dpi.S(22));
        }

        public void SetMosque(string name, string place)
        {
            _name = name;
            _place = place;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var icon = Theme.AppIcon;
            if (icon != null)
            {
                using var scaled = new Icon(icon, Dpi.Sz(32, 32));
                g.DrawIcon(scaled, Dpi.R(16, 20, 32, 32));
            }

            var textWidth = Width - Dpi.S(58) - Dpi.S(92);
            TextRenderer.DrawText(g, _name, Theme.H1, new Rectangle(Dpi.S(58), Dpi.S(18), textWidth, Dpi.S(22)),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _place ?? "", Theme.Small,
                new Rectangle(Dpi.S(58), Dpi.S(40), textWidth, Dpi.S(18)),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            using var pen = new Pen(Theme.Line);
            g.DrawLine(pen, Dpi.S(12), Height - 1, Width - Dpi.S(12), Height - 1);
        }
    }

    private sealed class CountdownCard : Control
    {
        private string _title = "NEXT PRAYER";
        private string _prayer = "--";
        private string _big = "--:--:--";
        private string _sub = "";

        public CountdownCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
        }

        public void Set(string title, string prayer, string big, string sub)
        {
            if (_title == title && _prayer == prayer && _big == big && _sub == sub) return;
            _title = title;
            _prayer = prayer;
            _big = big;
            _sub = sub;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, Dpi.F(12)))
            using (var brush = new LinearGradientBrush(new RectangleF(0, 0, Width, Height),
                       Theme.Panel, Color.FromArgb(0x16, 0x2A, 0x2E), LinearGradientMode.Vertical))
                g.FillPath(brush, path);
            Theme.DrawRounded(g, r, Dpi.F(12), Theme.Line);

            TextRenderer.DrawText(g, _title, Theme.Label, new Rectangle(0, Dpi.S(12), Width, Dpi.S(16)), Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var hasBig = !string.IsNullOrEmpty(_big);

            TextRenderer.DrawText(g, _prayer, Theme.H1, new Rectangle(0, Dpi.S(30), Width, Dpi.S(22)), Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (hasBig)
                TextRenderer.DrawText(g, _big, Theme.Display, new Rectangle(0, Dpi.S(52), Width, Dpi.S(44)), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, _sub ?? "", Theme.Small,
                new Rectangle(Dpi.S(8), Dpi.S(hasBig ? 96 : 66), Width - Dpi.S(16), Dpi.S(20)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
