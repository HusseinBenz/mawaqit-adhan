using System.Diagnostics;
using MawaqitAdhan.Core;

namespace MawaqitAdhan.Ui;

public sealed class SettingsForm : ThemedForm
{
    private readonly AppSettings _s;
    private readonly AdhanPlayback _playback;
    private readonly AudioPlayer _preview = new();
    private readonly CancellationTokenSource _downloads = new();
    private bool _closed;
    private bool _disposed;

    private readonly Panel _page;
    private int _y = 14;
    private bool _loading;

    private ComboBox _voiceCombo;
    private ComboBox _fajrCombo;
    private FlatButton _previewButton;
    private Label _voiceStatus;
    private Label _volumeValue;

    private sealed class VoiceItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public bool NeedsDownload { get; set; }
        public override string ToString() => NeedsDownload ? "Download:  " + Title : Title;
    }

    public SettingsForm(AppSettings settings, AdhanPlayback playback)
    {
        _s = settings;
        _playback = playback;

        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = Dpi.Sz(500, 586);

        _page = new Panel
        {
            Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height - Dpi.S(56)),
            BackColor = Theme.Background,
            AutoScroll = true
        };
        Controls.Add(_page);

        BuildAdhanSection();
        BuildPrayersSection();
        BuildOtherAudioSection();
        BuildNotificationsSection();
        BuildWindowsSection();
        BuildFooter();

        Theme.Apply(_page);
        FormClosing += (_, _) => { _closed = true; _downloads.Cancel(); _preview.Stop(); };
    }

    // ---- sections ------------------------------------------------------

    private void BuildAdhanSection()
    {
        Section("ADHAN");

        _voiceCombo = Combo("Adhan", out _);
        _voiceCombo.SelectedIndexChanged += (_, _) => OnVoicePicked(_voiceCombo, fajr: false);

        _fajrCombo = Combo("Fajr adhan", out _);
        _fajrCombo.SelectedIndexChanged += (_, _) => OnVoicePicked(_fajrCombo, fajr: true);

        _previewButton = new FlatButton { Text = "Preview", Bounds = Dpi.R(160, _y, 96, 28) };
        _previewButton.Click += (_, _) => TogglePreview();
        _page.Controls.Add(_previewButton);

        var add = new FlatButton { Text = "Add mp3...", Bounds = Dpi.R(264, _y, 104, 28) };
        add.Click += (_, _) => ImportFile();
        _page.Controls.Add(add);

        var folder = new FlatButton { Text = "Folder", Bounds = Dpi.R(376, _y, 80, 28) };
        folder.Click += (_, _) => OpenFolder(AppPaths.UserAudioDir);
        _page.Controls.Add(folder);

        _y += 34;

        _voiceStatus = Note("");
        ReloadVoices();

        // volume
        var label = new Label
        {
            Text = "Volume",
            Bounds = Dpi.R(16, _y + 4, 140, 20),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        _page.Controls.Add(label);

        var bar = new TrackBar
        {
            Bounds = Dpi.R(156, _y, 240, 32),
            Minimum = 0,
            Maximum = 100,
            TickStyle = TickStyle.None,
            Value = Numbers.Clamp(_s.Volume, 0, 100),
            BackColor = Theme.Background
        };
        bar.ValueChanged += (_, _) =>
        {
            _s.Volume = bar.Value;
            _volumeValue.Text = bar.Value + "%";
            _preview.SetVolume(bar.Value);
            _playback?.SetVolume(bar.Value);
        };
        _page.Controls.Add(bar);

        _volumeValue = new Label
        {
            Text = _s.Volume + "%",
            Bounds = Dpi.R(404, _y + 4, 60, 20),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent
        };
        _page.Controls.Add(_volumeValue);
        _y += 40;
    }

    private void BuildPrayersSection()
    {
        Section("PLAY THE ADHAN FOR");

        var names = new[] { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };
        var x = 16;
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var check = new FlatCheckBox
            {
                Text = names[i],
                Checked = _s.AdhanEnabled[i],
                Bounds = Dpi.R(x, _y, 90, 24)
            };
            check.CheckedChanged += (_, _) => _s.AdhanEnabled[index] = check.Checked;
            _page.Controls.Add(check);
            x += 92;
        }
        _y += 34;
    }

    private void BuildOtherAudioSection()
    {
        Section("WHILE THE ADHAN PLAYS");

        var duck = Check("Fade out and pause whatever else is playing", _s.DuckOtherAudio,
            v => _s.DuckOtherAudio = v);

        var onlyWhen = Check("Only when something is actually playing", _s.OnlyDuckWhenAudioPlaying,
            v => _s.OnlyDuckWhenAudioPlaying = v, indent: 34);

        var fadeOut = Decimal("Fade out over", _s.FadeOutSeconds, v => _s.FadeOutSeconds = v, "seconds", indent: 34);

        var pause = Check("Press play/pause so media actually stops", _s.PauseOtherAudio,
            v => _s.PauseOtherAudio = v, indent: 34);

        var resumeDelay = Decimal("Resume", _s.ResumeDelaySeconds, v => _s.ResumeDelaySeconds = v,
            "seconds after the adhan ends", indent: 34);

        var fadeIn = Decimal("Fade back in over", _s.FadeInSeconds, v => _s.FadeInSeconds = v, "seconds", indent: 34);

        Note("The adhan itself plays at your normal system volume - only the other audio is faded.");

        void Sync()
        {
            foreach (var c in new Control[] { onlyWhen, fadeOut, pause, resumeDelay, fadeIn })
                c.Enabled = duck.Checked;
            resumeDelay.Enabled = duck.Checked && pause.Checked;
            fadeIn.Enabled = duck.Checked && pause.Checked;
        }

        duck.CheckedChanged += (_, _) => Sync();
        pause.CheckedChanged += (_, _) => Sync();
        Sync();
    }

    private void BuildNotificationsSection()
    {
        Section("NOTIFICATIONS");

        var show = Check("Show a notification at prayer time", _s.ShowNotifications, v => _s.ShowNotifications = v);

        var before = Check("Remind me before the adhan", _s.NotifyBeforeAdhan, v => _s.NotifyBeforeAdhan = v, indent: 34);
        var minutes = Integer("Remind", _s.NotifyBeforeMinutes, v => _s.NotifyBeforeMinutes = v,
            "minutes before", 1, 60, indent: 34);
        var iqama = Check("Notify at iqama time", _s.NotifyAtIqama, v => _s.NotifyAtIqama = v, indent: 34);

        void Sync()
        {
            before.Enabled = show.Checked;
            iqama.Enabled = show.Checked;
            minutes.Enabled = show.Checked && before.Checked;
        }

        show.CheckedChanged += (_, _) => Sync();
        before.CheckedChanged += (_, _) => Sync();
        Sync();
    }

    private void BuildWindowsSection()
    {
        Section("WINDOWS");

        var startup = Check("Start with Windows", Startup.IsEnabled(), v =>
        {
            _s.StartWithWindows = v;
            if (!Startup.SetEnabled(v))
                MessageBox.Show(this, "Windows could not update the startup entry. See the log for details.", "Start with Windows");
            _s.StartWithWindows = Startup.IsEnabled();
        });
        startup.Checked = Startup.IsEnabled();

        Check("Start minimised to the tray", _s.StartMinimized, v => _s.StartMinimized = v, indent: 34);
        Check("Minimise to the tray", _s.MinimizeToTray, v => _s.MinimizeToTray = v);
        Check("Close to the tray instead of quitting", _s.CloseToTray, v => _s.CloseToTray = v);
    }

    private void BuildFooter()
    {
        var log = new FlatButton { Text = "Open log", Bounds = new Rectangle(Dpi.S(16), ClientSize.Height - Dpi.S(44), Dpi.S(96), Dpi.S(30)) };
        log.Click += (_, _) => OpenFolder(AppPaths.DataDir);
        Controls.Add(log);

        var done = new FlatButton { Text = "Done", Bounds = new Rectangle(ClientSize.Width - Dpi.S(116), ClientSize.Height - Dpi.S(44), Dpi.S(100), Dpi.S(30)) };
        done.MakePrimary();
        done.Click += (_, _) => Close();
        Controls.Add(done);

        AcceptButton = done;
    }

    // ---- adhan voices --------------------------------------------------

    private void ReloadVoices()
    {
        _loading = true;
        try
        {
            var available = AudioLibrary.Available();
            var downloadable = AudioLibrary.Downloadable();

            Fill(_voiceCombo, available, downloadable, _s.AdhanVoice);
            Fill(_fajrCombo, available, downloadable, _s.FajrAdhanVoice ?? _s.AdhanVoice);

            if (available.Count == 0)
                _voiceStatus.Text = "No adhan files yet - pick one from the download list above.";
            else
                _voiceStatus.Text = $"{available.Count} adhan{(available.Count == 1 ? "" : "s")} on this PC.";
        }
        finally
        {
            _loading = false;
        }

        static void Fill(ComboBox combo, List<AdhanVoice> available,
            List<(string Id, string Title)> downloadable, string selectedId)
        {
            combo.Items.Clear();

            foreach (var v in available)
                combo.Items.Add(new VoiceItem { Id = v.Id, Title = v.Title });

            foreach (var (id, title) in downloadable)
                combo.Items.Add(new VoiceItem { Id = id, Title = title, NeedsDownload = true });

            for (var i = 0; i < combo.Items.Count; i++)
                if (combo.Items[i] is VoiceItem v && !v.NeedsDownload &&
                    string.Equals(v.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }

            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }
    }

    private async void OnVoicePicked(ComboBox combo, bool fajr)
    {
        if (_loading || combo.SelectedItem is not VoiceItem item) return;

        if (!item.NeedsDownload)
        {
            Assign(item.Id, fajr);
            return;
        }

        _voiceCombo.Enabled = _fajrCombo.Enabled = false;
        _voiceStatus.Text = $"Downloading {item.Title}...";

        try
        {
            var progress = new Progress<int>(p => { if (!_closed) _voiceStatus.Text = $"Downloading {item.Title}... {p}%"; });
            await MawaqitClient.DownloadVoiceAsync(item.Id, progress, _downloads.Token);
            if (_closed) return;
            Assign(item.Id, fajr);
            ReloadVoices();
            _voiceStatus.Text = $"{item.Title} is ready.";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            Log.Error("Voice download failed", ex);
            ReloadVoices();
            _voiceStatus.Text = "Download failed: " + ex.Message;
        }
        finally
        {
            if (!_closed) _voiceCombo.Enabled = _fajrCombo.Enabled = true;
        }
    }

    private void Assign(string id, bool fajr)
    {
        if (fajr) _s.FajrAdhanVoice = id;
        else _s.AdhanVoice = id;
    }

    private void TogglePreview()
    {
        if (_preview.IsPlaying)
        {
            _preview.Stop();
            _previewButton.Text = "Preview";
            return;
        }

        var id = (_voiceCombo.SelectedItem as VoiceItem)?.Id ?? _s.AdhanVoice;
        var path = AppPaths.ResolveAudio(id);
        if (path == null)
        {
            _voiceStatus.Text = "That adhan is not on this PC yet.";
            return;
        }

        var error = _preview.Play(path, _s.Volume);
        _voiceStatus.Text = error ?? $"Playing {AudioLibrary.PrettyName(id)}";
        _previewButton.Text = error == null ? "Stop" : "Preview";
    }

    private void ImportFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an adhan mp3",
            Filter = "MP3 audio (*.mp3)|*.mp3|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var voice = AudioLibrary.Import(dialog.FileName);
            _s.AdhanVoice = voice.Id;
            ReloadVoices();
            _voiceStatus.Text = $"Added {voice.Title}.";
        }
        catch (Exception ex)
        {
            Log.Error("Could not import the adhan file", ex);
            _voiceStatus.Text = "Could not add that file: " + ex.Message;
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            AppPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open " + path + ": " + ex.Message);
        }
    }

    // ---- tiny layout helpers -------------------------------------------

    private void Section(string title)
    {
        _y += 8;
        var label = new Label
        {
            Text = title,
            Font = Theme.Label,
            ForeColor = Theme.Faint,
            Bounds = Dpi.R(16, _y, 400, 18),
            BackColor = Color.Transparent
        };
        _page.Controls.Add(label);
        _y += 20;

        var rule = new Panel
        {
            Bounds = new Rectangle(Dpi.S(16), Dpi.S(_y), _page.ClientSize.Width - Dpi.S(48), 1),
            BackColor = Theme.Line
        };
        _page.Controls.Add(rule);
        _y += 10;
    }

    private CheckBox Check(string text, bool value, Action<bool> setter, int indent = 16)
    {
        var check = new FlatCheckBox
        {
            Text = text,
            Checked = value,
            Bounds = new Rectangle(Dpi.S(indent), Dpi.S(_y), _page.ClientSize.Width - Dpi.S(indent + 34), Dpi.S(24))
        };
        check.CheckedChanged += (_, _) => setter(check.Checked);
        _page.Controls.Add(check);
        _y += 28;
        return check;
    }

    private NumberBox Decimal(string prefix, double value, Action<double> setter, string suffix, int indent = 16)
    {
        var box = Numeric(prefix, suffix, indent);
        box.Decimals = 1;
        box.Step = 0.5;
        box.Minimum = 0;
        box.Maximum = 120;
        box.Value = Numbers.Clamp(value, 0, 120);
        box.ValueChanged += (_, _) => setter(box.Value);
        return box;
    }

    private NumberBox Integer(string prefix, int value, Action<int> setter, string suffix,
        int min, int max, int indent = 16)
    {
        var box = Numeric(prefix, suffix, indent);
        box.Decimals = 0;
        box.Step = 1;
        box.Minimum = min;
        box.Maximum = max;
        box.Value = Numbers.Clamp(value, min, max);
        box.ValueChanged += (_, _) => setter((int)Math.Round(box.Value));
        return box;
    }

    private NumberBox Numeric(string prefix, string suffix, int indent)
    {
        var prefixLabel = new Label
        {
            Text = prefix,
            Bounds = Dpi.R(indent, _y + 3, 150, 20),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        _page.Controls.Add(prefixLabel);

        var box = new NumberBox { Bounds = Dpi.R(indent + 154, _y, 68, 26) };
        _page.Controls.Add(box);

        var suffixLabel = new Label
        {
            Text = suffix,
            Bounds = new Rectangle(Dpi.S(indent + 230), Dpi.S(_y + 4),
                _page.ClientSize.Width - Dpi.S(indent + 230 + 34), Dpi.S(20)),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        _page.Controls.Add(suffixLabel);

        _y += 32;
        return box;
    }

    private ComboBox Combo(string caption, out Label label)
    {
        label = new Label
        {
            Text = caption,
            Bounds = Dpi.R(16, _y + 4, 140, 20),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        _page.Controls.Add(label);

        var combo = new DarkComboBox { Bounds = Dpi.R(160, _y, 296, 26) };
        _page.Controls.Add(combo);

        _y += 32;
        return combo;
    }

    private Label Note(string text)
    {
        var label = new Label
        {
            Text = text,
            Bounds = new Rectangle(Dpi.S(16), Dpi.S(_y), _page.ClientSize.Width - Dpi.S(48), Dpi.S(32)),
            ForeColor = Theme.Faint,
            Font = Theme.Small,
            BackColor = Color.Transparent
        };
        _page.Controls.Add(label);
        _y += 30;
        return label;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _closed = true;
            _downloads.Cancel();
            _downloads.Dispose();
            _preview.Dispose();
        }
        base.Dispose(disposing);
    }
}
