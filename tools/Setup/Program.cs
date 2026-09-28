using System.Diagnostics;
using System.Reflection;

namespace MawaqitAdhan.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var silent = args.Contains("--silent");
        try
        {
            var context = new InstallContext();
            var installer = new Installer(context);
            if (args.Contains("--uninstall"))
            {
                if (MessageBox.Show("Uninstall Mawaqit Adhan? Your settings, prayer cache, and imported voices will be kept.",
                    "Mawaqit Adhan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                var helper = Path.Combine(Path.GetTempPath(), "MawaqitAdhan-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, helper);
                Process.Start(new ProcessStartInfo(helper, "--uninstall-worker " + Process.GetCurrentProcess().Id + " \"" + AppContext.BaseDirectory.TrimEnd('\\') + "\"") { UseShellExecute = false });
                return 0;
            }
            if (args.Length == 3 && args[0] == "--uninstall-worker")
            {
                try { using var parent = Process.GetProcessById(int.Parse(args[1])); if (!parent.WaitForExit(15000)) throw new IOException("The uninstaller is still running."); }
                catch (ArgumentException) { }
                using var uninstallMutex = new Mutex(true, @"Local\MawaqitAdhan.Setup", out var canUninstall);
                if (!canUninstall) throw new IOException("Another Mawaqit Adhan installer is running.");
                installer.Uninstall(args[2]);
                MessageBox.Show("Mawaqit Adhan was removed. Your settings and voices have been kept.", "Mawaqit Adhan");
                return 0;
            }
            using var mutex = new Mutex(true, @"Local\MawaqitAdhan.Setup", out var first);
            if (!first) throw new IOException("Another Mawaqit Adhan installer is running.");
            var directory = installer.DetectDirectory();
            var dirIndex = Array.IndexOf(args, "--dir");
            if (dirIndex >= 0)
            {
                if (dirIndex + 1 >= args.Length) throw new ArgumentException("--dir requires a folder path.");
                directory = args[dirIndex + 1];
            }
            if (silent)
            {
                Install(installer, directory);
                return 0;
            }
            Application.Run(new SetupForm(installer, directory));
            return 0;
        }
        catch (Exception ex)
        {
            var log = Path.Combine(Path.GetTempPath(), "MawaqitAdhan-setup-error.log");
            File.WriteAllText(log, DateTime.Now + "\n" + ex);
            if (!silent) MessageBox.Show(ex.Message + "\n\nDetails: " + log, "Setup could not finish", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static void Install(Installer installer, string directory)
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        if (payload == null) throw new InvalidDataException("This setup program does not contain an application package.");
        installer.Install(payload, directory, Application.ExecutablePath);
    }
}

internal sealed class SetupForm : Form
{
    private readonly Installer _installer;
    private readonly TextBox _directory;
    private readonly Button _install;
    private readonly Button _browse;
    private readonly Label _status;
    private readonly CheckBox _launch;
    private bool _working;

    public SetupForm(Installer installer, string directory)
    {
        _installer = installer;
        Text = "Mawaqit Adhan Setup — " + Installer.Version;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(590, 295);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var detected = Installer.IsApp(Path.Combine(directory, "MawaqitAdhan.exe"));
        Controls.Add(new Label { Text = detected ? "Update Mawaqit Adhan" : "Install Mawaqit Adhan", Font = new Font("Segoe UI", 17, FontStyle.Bold), Bounds = new Rectangle(24, 20, 540, 38) });
        Controls.Add(new Label { Text = detected ? "An existing copy was found. It will be updated in this folder." : "Install for your Windows account. Administrator access is not needed.", Bounds = new Rectangle(24, 65, 540, 42) });
        _directory = new TextBox { Text = directory, Bounds = new Rectangle(24, 114, 440, 30) };
        _browse = new Button { Text = "Browse…", Bounds = new Rectangle(472, 112, 94, 32) };
        _browse.Click += (_, _) => { using var picker = new FolderBrowserDialog { SelectedPath = _directory.Text, Description = "Choose the existing app folder to update, or a new installation folder." }; if (picker.ShowDialog(this) == DialogResult.OK) _directory.Text = picker.SelectedPath; };
        _status = new Label { Text = "Settings, cached prayer times, and imported voices are preserved.", Bounds = new Rectangle(24, 157, 540, 42) };
        _launch = new CheckBox { Text = "Launch after installation", Checked = true, Bounds = new Rectangle(24, 226, 310, 32) };
        _install = new Button { Text = detected ? "Update" : "Install", Bounds = new Rectangle(446, 221, 120, 38) };
        _install.Click += async (_, _) =>
        {
            _working = true;
            _install.Enabled = _browse.Enabled = _directory.Enabled = false;
            _status.Text = "Installing… The running app will close safely before files are replaced.";
            var target = _directory.Text;
            try
            {
                await Task.Run(() => Program.Install(_installer, target));
                _status.Text = "Installation complete.";
                if (_launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(target, "MawaqitAdhan.exe"), "--tray") { UseShellExecute = true });
                _working = false;
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Installation did not finish. You can correct the problem and try again.";
                MessageBox.Show(this, ex.Message, "Setup could not finish", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _working = false;
                _install.Enabled = _browse.Enabled = _directory.Enabled = true;
            }
        };
        Controls.AddRange(new Control[] { _directory, _browse, _status, _launch, _install });
        FormClosing += (_, e) => { if (_working) e.Cancel = true; };
    }
}
