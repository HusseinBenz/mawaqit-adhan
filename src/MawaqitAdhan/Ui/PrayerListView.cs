using System.Drawing.Drawing2D;
using MawaqitAdhan.Core;

namespace MawaqitAdhan.Ui;

/// <summary>The day's timetable: one painted row per prayer, with the next one lit up.</summary>
public sealed class PrayerListView : Control
{
    private static int HeaderHeight => Dpi.S(22);
    private static int RowHeight => Dpi.S(40);

    private List<PrayerEvent> _prayers = new();
    private Prayer? _next;
    private Prayer? _current;
    private bool[] _adhanEnabled = { true, true, true, true, true };

    public PrayerListView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
    }

    public int PreferredHeight => HeaderHeight + RowHeight * Math.Max(_prayers.Count, 6) + Dpi.S(8);

    public void SetDay(List<PrayerEvent> prayers, Prayer? next, Prayer? current, bool[] adhanEnabled)
    {
        _prayers = prayers ?? new List<PrayerEvent>();
        _next = next;
        _current = current;
        if (adhanEnabled is { Length: 5 }) _adhanEnabled = adhanEnabled;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        DrawHeader(g);

        if (_prayers.Count == 0)
        {
            TextRenderer.DrawText(g, "No timetable for today yet.", Theme.Body,
                new Rectangle(0, HeaderHeight, Width, RowHeight * 2), Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var y = HeaderHeight;
        foreach (var p in _prayers)
        {
            DrawRow(g, p, new Rectangle(0, y, Width, RowHeight));
            y += RowHeight;
        }
    }

    private void DrawHeader(Graphics g)
    {
        var r = new Rectangle(0, 0, Width, HeaderHeight);
        TextRenderer.DrawText(g, "PRAYER", Theme.Label, new Rectangle(Dpi.S(16), r.Y, Dpi.S(120), r.Height),
            Theme.Faint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "ADHAN", Theme.Label, new Rectangle(Width - Dpi.S(190), r.Y, Dpi.S(90), r.Height),
            Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "IQAMA", Theme.Label, new Rectangle(Width - Dpi.S(96), r.Y, Dpi.S(80), r.Height),
            Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        using var pen = new Pen(Theme.Line);
        g.DrawLine(pen, Dpi.S(12), r.Bottom - 1, Width - Dpi.S(12), r.Bottom - 1);
    }

    private void DrawRow(Graphics g, PrayerEvent p, Rectangle row)
    {
        var isNext = _next.HasValue && p.Prayer == _next.Value;
        var isCurrent = !isNext && _current.HasValue && p.Prayer == _current.Value;
        var isSunrise = p.Prayer == Prayer.Sunrise;

        if (isNext)
        {
            var card = new RectangleF(Dpi.S(10), row.Y + Dpi.S(3), row.Width - Dpi.S(20), row.Height - Dpi.S(6));
            Theme.FillRounded(g, card, Dpi.F(8), Theme.AccentSoft);
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, Dpi.S(10), row.Y + Dpi.S(9), Dpi.S(3), row.Height - Dpi.S(18));
        }
        else if (isCurrent)
        {
            var card = new RectangleF(Dpi.S(10), row.Y + Dpi.S(3), row.Width - Dpi.S(20), row.Height - Dpi.S(6));
            Theme.FillRounded(g, card, Dpi.F(8), Theme.Panel);
        }

        var nameColour = isSunrise ? Theme.Muted : isNext ? Theme.Text : Theme.Text;
        var timeColour = isNext ? Theme.Accent : isSunrise ? Theme.Muted : Theme.Text;

        TextRenderer.DrawText(g, p.Name, isNext ? Theme.H2 : Theme.Body,
            new Rectangle(Dpi.S(24), row.Y, Dpi.S(110), row.Height), nameColour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, p.NameArabic, Theme.Arabic,
            new Rectangle(Dpi.S(126), row.Y, Dpi.S(96), row.Height), isSunrise ? Theme.Faint : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.RightToLeft);

        TextRenderer.DrawText(g, p.AdhanClock ?? "--:--", Theme.Clock,
            new Rectangle(Width - Dpi.S(190), row.Y, Dpi.S(90), row.Height), timeColour,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, p.IqamaClock ?? (isSunrise ? "" : "-"), Theme.Body,
            new Rectangle(Width - Dpi.S(96), row.Y, Dpi.S(80), row.Height), Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        // a small dot marks the prayers whose adhan will actually be played
        if (p.CallsAdhan && p.AdhanSlot >= 0)
        {
            var on = _adhanEnabled[p.AdhanSlot];
            var dot = new Rectangle(Dpi.S(14), row.Y + row.Height / 2 - Dpi.S(3), Dpi.S(6), Dpi.S(6));
            if (on)
            {
                using var b = new SolidBrush(isNext ? Theme.Accent : Theme.Faint);
                g.FillEllipse(b, dot);
            }
            else
            {
                using var pen = new Pen(Theme.Faint);
                g.DrawEllipse(pen, dot);
            }
        }
    }
}
