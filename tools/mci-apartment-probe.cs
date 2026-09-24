using System.Runtime.InteropServices;
using System.Text;

internal static class P
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int Send(string cmd, StringBuilder buf, int n, IntPtr h);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    private static extern bool Err(int code, StringBuilder buf, int n);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr p, int flags);

    private static string Run(string cmd)
    {
        var b = new StringBuilder(512);
        var r = Send(cmd, b, 512, IntPtr.Zero);
        if (r == 0) return "OK " + b;
        var e = new StringBuilder(512);
        Err(r, e, 512);
        return $"ERR({r}) {e}";
    }

    private static void Try(string label, string alias)
    {
        var file = @"C:\Users\hamza\Desktop\adhan\assets\audio\bip.mp3";
        var apartment = Thread.CurrentThread.GetApartmentState();
        Console.WriteLine($"{label,-34} apartment={apartment,-13} open: {Run($"open \"{file}\" type mpegvideo alias {alias}")}");
        Run($"close {alias}");
    }

    private static async Task Main()
    {
        Try("main thread", "a1");

        await Task.Run(() => Try("Task.Run (thread pool)", "a2"));

        var mta = new Thread(() => Try("explicit MTA thread", "a3"));
        mta.SetApartmentState(ApartmentState.MTA);
        mta.Start();
        mta.Join();

        var sta = new Thread(() => Try("explicit STA thread", "a4"));
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        await Task.Run(() =>
        {
            CoInitializeEx(IntPtr.Zero, 0x0); // COINIT_APARTMENTTHREADED
            Try("pool thread + CoInitialize STA", "a5");
        });

        await Task.Run(() =>
        {
            CoInitializeEx(IntPtr.Zero, 0x2); // COINIT_MULTITHREADED
            Try("pool thread + CoInitialize MTA", "a6");
        });
    }
}
