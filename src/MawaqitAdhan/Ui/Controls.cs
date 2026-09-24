using System.Drawing.Drawing2D;

using MawaqitAdhan.Core;

namespace MawaqitAdhan.Ui;

/// <summary>A drop-down that stays dark: the closed box, the border, the arrow and the list.</summary>
public sealed class DarkComboBox : ComboBox
{
    private const int WmPaint = 0x000F;

    public DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = Theme.PanelHi;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ItemHeight = Dpi.S(20);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        var isList = (e.State & DrawItemState.ComboBoxEdit) != DrawItemState.ComboBoxEdit;
        var highlighted = isList && (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var back = new SolidBrush(highlighted ? Theme.AccentSoft : Theme.PanelHi))
            g.FillRectangle(back, e.Bounds);

        var text = Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(g, text, Font,
            new Rectangle(e.Bounds.X + Dpi.S(4), e.Bounds.Y, e.Bounds.Width - Dpi.S(26), e.Bounds.Height),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmPaint) return;

        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // paint over the system-drawn arrow button and frame
        var buttonWidth = Dpi.S(20);
        using (var back = new SolidBrush(Theme.PanelHi))
            g.FillRectangle(back, Width - buttonWidth - 1, 1, buttonWidth, Height - 2);

        var cx = Width - buttonWidth / 2 - 1;
        var cy = Height / 2;
        var a = Dpi.S(4);
        using (var arrow = new SolidBrush(Theme.Muted))
            g.FillPolygon(arrow, new[]
            {
                new Point(cx - a, cy - a / 2),
                new Point(cx + a, cy - a / 2),
                new Point(cx, cy + a)
            });

        using var pen = new Pen(Theme.Line);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>
/// A small themed number field. Click or scroll the arrows to change the value -
/// enough for a handful of seconds and minutes, and it matches the rest of the window.
/// </summary>
public sealed class NumberBox : Control
{
    private double _value;
    private bool _hoverUp;
    private bool _hoverDown;

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Step { get; set; } = 1;
    public int Decimals { get; set; }
    public string Suffix { get; set; } = "";

    public event EventHandler ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            var clamped = Numbers.Clamp(Math.Round(value, Decimals), Minimum, Maximum);
            if (Math.Abs(clamped - _value) < 0.0001) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NumberBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    private Rectangle UpArea => new(Width - Dpi.S(18), 1, Dpi.S(17), Height / 2 - 1);
    private Rectangle DownArea => new(Width - Dpi.S(18), Height / 2, Dpi.S(17), Height / 2 - 1);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled) return;
        if (UpArea.Contains(e.Location)) Value += Step;
        else if (DownArea.Contains(e.Location)) Value -= Step;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var up = UpArea.Contains(e.Location);
        var down = DownArea.Contains(e.Location);
        if (up == _hoverUp && down == _hoverDown) return;
        _hoverUp = up;
        _hoverDown = down;
        Cursor = up || down ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverUp = _hoverDown = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Enabled) return;
        Value += e.Delta > 0 ? Step : -Step;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, r, Dpi.F(5), Enabled ? Theme.PanelHi : Theme.Panel);
        Theme.DrawRounded(g, r, Dpi.F(5), Theme.Line);

        var text = _value.ToString("F" + Decimals) + Suffix;
        TextRenderer.DrawText(g, text, Font,
            new Rectangle(Dpi.S(8), 0, Width - Dpi.S(26), Height),
            Enabled ? ForeColor : Theme.Faint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        DrawArrow(g, UpArea, true, _hoverUp);
        DrawArrow(g, DownArea, false, _hoverDown);
    }

    private void DrawArrow(Graphics g, Rectangle area, bool up, bool hover)
    {
        var colour = !Enabled ? Theme.Panel : hover ? Theme.Accent : Theme.Muted;
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        var a = Dpi.S(3);

        using var brush = new SolidBrush(colour);
        g.FillPolygon(brush, up
            ? new[] { new Point(cx - a, cy + a / 2), new Point(cx + a, cy + a / 2), new Point(cx, cy - a) }
            : new[] { new Point(cx - a, cy - a / 2), new Point(cx + a, cy - a / 2), new Point(cx, cy + a) });
    }
}
