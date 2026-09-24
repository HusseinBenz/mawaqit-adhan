namespace MawaqitAdhan.Ui;

/// <summary>
/// The layout below is written in plain 96-DPI pixels and scaled here instead of
/// leaning on WinForms auto-scaling, which double-scales hand-placed controls.
/// Fonts are declared in points, so those already follow the display on their own.
/// </summary>
public static class Dpi
{
    public static float Scale { get; private set; } = 1f;

    public static void Init()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            if (g.DpiX > 0) Scale = g.DpiX / 96f;
        }
        catch
        {
            Scale = 1f;
        }
    }

    public static int S(int value) => (int)Math.Round(value * Scale);

    public static float F(float value) => value * Scale;

    public static Size Sz(int width, int height) => new(S(width), S(height));

    public static Point P(int x, int y) => new(S(x), S(y));

    public static Rectangle R(int x, int y, int width, int height) => new(S(x), S(y), S(width), S(height));
}
