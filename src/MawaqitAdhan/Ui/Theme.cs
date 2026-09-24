using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MawaqitAdhan.Core;

namespace MawaqitAdhan.Ui;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x10, 0x14, 0x1A);
    public static readonly Color Panel = Color.FromArgb(0x1A, 0x20, 0x29);
    public static readonly Color PanelHi = Color.FromArgb(0x22, 0x2A, 0x35);
    public static readonly Color Line = Color.FromArgb(0x2B, 0x33, 0x40);
    public static readonly Color Accent = Color.FromArgb(0x2E, 0xC4, 0xA6);
    public static readonly Color AccentSoft = Color.FromArgb(0x1C, 0x4D, 0x48);
    public static readonly Color Text = Color.FromArgb(0xE9, 0xEE, 0xF4);
    public static readonly Color Muted = Color.FromArgb(0x8B, 0x97, 0xA7);
    public static readonly Color Faint = Color.FromArgb(0x5E, 0x6A, 0x7A);
    public static readonly Color Danger = Color.FromArgb(0xE3, 0x6F, 0x6F);

    public static readonly Font Display = new("Segoe UI Semibold", 30f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font H1 = new("Segoe UI Semibold", 12.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font H2 = new("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Body = new("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Small = new("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Clock = new("Segoe UI Semibold", 11.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Arabic = new("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Label = new("Segoe UI Semibold", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

    private static Icon _icon;

    public static Icon AppIcon
    {
        get
        {
            if (_icon != null) return _icon;
            try
            {
                var path = Path.Combine(AppPaths.AppDir, "app.ico");
                if (File.Exists(path)) return _icon = new Icon(path);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load the app icon: " + ex.Message);
            }
            return _icon = DrawFallbackIcon();
        }
    }

    private static Icon DrawFallbackIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var back = new SolidBrush(Accent);
            g.FillEllipse(back, 0, 0, 31, 31);
            using var fore = new SolidBrush(Background);
            g.FillEllipse(fore, 9, 5, 20, 20);
            using var cut = new SolidBrush(Accent);
            g.FillEllipse(cut, 14, 5, 20, 20);
        }
        var handle = bmp.GetHicon();
        try { using var icon = Icon.FromHandle(handle); return (Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0 || r.Width <= d || r.Height <= d)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
    {
        using var path = Rounded(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Asks Windows for a dark title bar, which older builds simply ignore.</summary>
    public static void UseDarkTitleBar(IntPtr handle)
    {
        try
        {
            var on = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; 19 was its number on Windows 10 1809-1903
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
        }
        catch
        {
            // not fatal - the window just keeps the light title bar
        }
    }

    /// <summary>Applies the dark palette to a standard control tree.</summary>
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case TextBox tb:
                    tb.BackColor = PanelHi;
                    tb.ForeColor = Text;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case CheckBox cb:
                    cb.ForeColor = Text;
                    cb.BackColor = Color.Transparent;
                    cb.FlatStyle = FlatStyle.Flat;
                    break;
                case RadioButton rb:
                    rb.ForeColor = Text;
                    rb.BackColor = Color.Transparent;
                    rb.FlatStyle = FlatStyle.Flat;
                    break;
                case Label lb:
                    if (lb.ForeColor == SystemColors.ControlText) lb.ForeColor = Text;
                    lb.BackColor = Color.Transparent;
                    break;
                case ComboBox cbx:
                    cbx.BackColor = PanelHi;
                    cbx.ForeColor = Text;
                    cbx.FlatStyle = FlatStyle.Flat;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = PanelHi;
                    nud.ForeColor = Text;
                    nud.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListBox lbx:
                    lbx.BackColor = Panel;
                    lbx.ForeColor = Text;
                    lbx.BorderStyle = BorderStyle.None;
                    break;
                case TrackBar tbar:
                    tbar.BackColor = Background;
                    break;
                case Panel p:
                    if (p.BackColor == SystemColors.Control) p.BackColor = Background;
                    break;
                case GroupBox gb:
                    gb.ForeColor = Muted;
                    gb.BackColor = Color.Transparent;
                    break;
            }

            if (c.HasChildren) Apply(c);
        }
    }
}

/// <summary>A flat, themed button that behaves itself on a dark background.</summary>
public sealed class FlatButton : Button
{
    private bool _hover;

    public Color Fill { get; set; } = Theme.PanelHi;
    public Color FillHover { get; set; } = Color.FromArgb(0x2C, 0x35, 0x43);
    public Color Border { get; set; } = Theme.Line;
    public int Radius { get; set; } = 7;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    public void MakePrimary()
    {
        Fill = Theme.Accent;
        FillHover = Color.FromArgb(0x3A, 0xD6, 0xB7);
        Border = Theme.Accent;
        ForeColor = Color.FromArgb(0x06, 0x1A, 0x17);
        Font = Theme.H2;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var fill = !Enabled ? Theme.Panel : _hover ? FillHover : Fill;

        Theme.FillRounded(g, r, Dpi.F(Radius), fill);
        Theme.DrawRounded(g, r, Dpi.F(Radius), Enabled ? Border : Theme.Line);

        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r),
            Enabled ? ForeColor : Theme.Faint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A check box that is actually visible on a dark background.</summary>
public sealed class FlatCheckBox : CheckBox
{
    public FlatCheckBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var size = Dpi.S(15);
        var box = new RectangleF(Dpi.S(1), (Height - size) / 2f, size, size);

        if (Checked)
        {
            Theme.FillRounded(g, box, Dpi.F(3), Enabled ? Theme.Accent : Theme.Faint);
            using var pen = new Pen(Theme.Background, Dpi.F(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(box.X + size * 0.24f, box.Y + size * 0.52f),
                new PointF(box.X + size * 0.44f, box.Y + size * 0.72f),
                new PointF(box.X + size * 0.77f, box.Y + size * 0.28f)
            });
        }
        else
        {
            Theme.FillRounded(g, box, Dpi.F(3), Theme.PanelHi);
            Theme.DrawRounded(g, box, Dpi.F(3), Enabled ? Theme.Line : Theme.Panel);
        }

        var textLeft = (int)box.Right + Dpi.S(9);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textLeft, 0, Width - textLeft, Height),
            Enabled ? ForeColor : Theme.Faint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Every window in the app: dark title bar, dark background.</summary>
public class ThemedForm : Form
{
    protected ThemedForm()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.None;
        Icon = Theme.AppIcon;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(Handle);
    }
}
