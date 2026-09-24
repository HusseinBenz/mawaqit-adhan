using System.Runtime.InteropServices;
using MawaqitAdhan.Core;
using MawaqitAdhan.Ui;

namespace MawaqitAdhan;

internal static class Program
{
    private const int HwndBroadcast = 0xFFFF;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static void Main(string[] args)
    {
        AppPaths.EnsureCreated();

        // one copy only - a second launch just brings the first one forward
        using var mutex = new Mutex(true, @"Local\MawaqitAdhan.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            var show = RegisterWindowMessage("MawaqitAdhan.ShowWindow");
            if (show != 0) PostMessage(new IntPtr(HwndBroadcast), show, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Fail(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail(e.ExceptionObject as Exception);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Dpi.Init();

        var settings = AppSettings.Load();
        var startHidden = settings.StartMinimized ||
                          args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "-t", StringComparison.OrdinalIgnoreCase));

        Log.Info($"Mawaqit Adhan starting (hidden: {startHidden})");

        try
        {
            Application.Run(new MainForm(settings, startHidden));
        }
        finally
        {
            Log.Info("Mawaqit Adhan closed");
        }
    }

    private static void Fail(Exception ex)
    {
        Log.Error("Unhandled exception", ex);
        MessageBox.Show(
            (ex?.Message ?? "Something went wrong.") + "\n\nDetails were written to:\n" + AppPaths.LogFile,
            "Mawaqit Adhan", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
