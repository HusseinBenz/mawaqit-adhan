using MawaqitAdhan.Core;

namespace MawaqitAdhan.Ui;

/// <summary>Search mawaqit for a masjid - by name, by city, or by pasting its link.</summary>
public sealed class MosquePickerForm : ThemedForm
{
    private readonly TextBox _query;
    private readonly ListBox _results;
    private readonly Label _hint;
    private readonly FlatButton _searchButton;
    private readonly FlatButton _useButton;

    private List<MosqueSearchResult> _items = new();
    private CancellationTokenSource _cts;
    private bool _disposed;

    public MosqueSearchResult Selected { get; private set; }

    public MosquePickerForm(string currentSlug)
    {
        Text = "Choose a masjid";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = Dpi.Sz(520, 480);

        var title = new Label
        {
            Text = "Search mawaqit.net",
            Font = Theme.H1,
            ForeColor = Theme.Text,
            Bounds = Dpi.R(16, 14, 400, 24),
            BackColor = Color.Transparent
        };
        Controls.Add(title);

        var sub = new Label
        {
            Text = "Masjid name, city, or a link such as https://mawaqit.net/de/omar-witten",
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            Bounds = Dpi.R(16, 38, 490, 18),
            BackColor = Color.Transparent
        };
        Controls.Add(sub);

        _query = new TextBox
        {
            Bounds = Dpi.R(16, 64, 380, 28),
            BackColor = Theme.PanelHi,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Body
        };
        _query.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = SearchAsync();
        };
        Controls.Add(_query);

        _searchButton = new FlatButton { Text = "Search", Bounds = Dpi.R(404, 64, 100, 28) };
        _searchButton.MakePrimary();
        _searchButton.Click += (_, _) => _ = SearchAsync();
        Controls.Add(_searchButton);

        _results = new ListBox
        {
            Bounds = Dpi.R(16, 104, 488, 300),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = Dpi.S(50),
            IntegralHeight = false
        };
        _results.DrawItem += DrawResult;
        _results.DoubleClick += (_, _) => Use();
        _results.SelectedIndexChanged += (_, _) => _useButton.Enabled = _results.SelectedIndex >= 0;
        Controls.Add(_results);

        _hint = new Label
        {
            Bounds = Dpi.R(16, 412, 320, 40),
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_hint);

        var cancel = new FlatButton { Text = "Cancel", Bounds = Dpi.R(346, 420, 76, 32) };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);

        _useButton = new FlatButton { Text = "Use this", Bounds = Dpi.R(430, 420, 74, 32), Enabled = false };
        _useButton.MakePrimary();
        _useButton.Click += (_, _) => Use();
        Controls.Add(_useButton);

        AcceptButton = null;
        CancelButton = cancel;

        if (!string.IsNullOrWhiteSpace(currentSlug)) _query.Text = currentSlug;
        Shown += (_, _) => _query.Focus();
    }

    private async Task SearchAsync()
    {
        var text = _query.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetHint("Type something to search for.");
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _searchButton.Enabled = false;
        SetHint("Searching mawaqit...");
        _results.Items.Clear();
        _items.Clear();

        try
        {
            var slug = MawaqitClient.ExtractSlug(text);
            var looksLikeLink = text.IndexOf("mawaqit.net", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikeLink && slug != null)
            {
                var t = await MawaqitClient.FetchTimetableAsync(slug, token);
                _items = new List<MosqueSearchResult>
                {
                    new()
                    {
                        Slug = slug,
                        Name = t.Name,
                        Localisation = string.Join("   -   ",
                            new[] { t.CountryCode, t.Timezone }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    }
                };
            }
            else
            {
                _items = await MawaqitClient.SearchAsync(text, token);
            }

            if (token.IsCancellationRequested || IsDisposed) return;

            foreach (var m in _items) _results.Items.Add(m);

            if (_items.Count == 0)
            {
                SetHint("Nothing found. Try the city, or paste the masjid's mawaqit link.");
            }
            else
            {
                SetHint($"{_items.Count} masjid{(_items.Count == 1 ? "" : "s")} found.");
                _results.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // superseded
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || IsDisposed) return;
            Log.Error("Mosque search failed", ex);
            SetHint("Search failed: " + ex.Message);
        }
        finally
        {
            if (!token.IsCancellationRequested && !IsDisposed) _searchButton.Enabled = true;
        }
    }

    private void Use()
    {
        if (_results.SelectedIndex < 0 || _results.SelectedIndex >= _items.Count) return;
        Selected = _items[_results.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetHint(string text) => _hint.Text = text;

    private void DrawResult(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _items.Count) return;

        var item = _items[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var g = e.Graphics;

        using (var back = new SolidBrush(selected ? Theme.AccentSoft : Theme.Panel))
            g.FillRectangle(back, e.Bounds);

        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + Dpi.S(8), Dpi.S(3), e.Bounds.Height - Dpi.S(16));
        }

        TextRenderer.DrawText(g, item.Title, Theme.H2,
            new Rectangle(e.Bounds.X + Dpi.S(14), e.Bounds.Y + Dpi.S(6), e.Bounds.Width - Dpi.S(24), Dpi.S(20)),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var line = item.Subtitle;
        if (string.IsNullOrWhiteSpace(line)) line = item.Slug;

        TextRenderer.DrawText(g, line, Theme.Small,
            new Rectangle(e.Bounds.X + Dpi.S(14), e.Bounds.Y + Dpi.S(27), e.Bounds.Width - Dpi.S(24), Dpi.S(18)),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (item.Times is { Length: >= 5 })
        {
            var times = string.Join("  ", item.Times.Skip(item.Times.Length == 6 ? 1 : 0).Take(5));
            TextRenderer.DrawText(g, times, Theme.Small,
                new Rectangle(e.Bounds.Right - Dpi.S(230), e.Bounds.Y + Dpi.S(6), Dpi.S(216), Dpi.S(18)),
                Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _cts?.Cancel(); _cts?.Dispose(); }
        base.Dispose(disposing);
    }
}
