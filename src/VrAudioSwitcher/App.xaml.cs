using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using VrAudioSwitcher.Core;
using Application = System.Windows.Application;

namespace VrAudioSwitcher;

public partial class App : Application
{
    private Mutex? _mutex;
    private NotifyIcon? _tray;
    private SwitchController? _controller;
    private MainWindow? _window;
    private ToolStripMenuItem? _status;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, "VrAudioSwitcher.SingleInstance", out var first);
        if (!first) { Shutdown(); return; }

        var config = AppConfig.Load();
        _controller = new SwitchController(config);

        var menu = new ContextMenuStrip();
        _status = new ToolStripMenuItem("Starting...") { Enabled = false };
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open settings", null, (_, _) => ShowWindow());
        menu.Items.Add("Auto (follow SteamVR)", null, (_, _) => SetOverride(ModeOverride.Auto));
        menu.Items.Add("Force Desktop", null, (_, _) => SetOverride(ModeOverride.Desktop));
        menu.Items.Add("Force VR", null, (_, _) => SetOverride(ModeOverride.Vr));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Icon = MakeIcon(false) };
        _tray.MouseClick += (_, a) => { if (a.Button == MouseButtons.Left) ShowWindow(); };
        _controller.ModeChanged += () => Dispatcher.BeginInvoke(RefreshTray);
        RefreshTray();

        var firstRun = config.Desktop.OutputId == null && config.Vr.OutputId == null;
        if (firstRun || !config.StartMinimized || e.Args.Contains("--show")) ShowWindow();
        else Trim();
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr h);

    // Return memory to the OS once the UI is gone: tray idle sits at a few MB.
    private static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
    }

    private void SetOverride(ModeOverride mode)
    {
        _controller!.Config.Override = mode;
        _controller.Config.Save();
        _controller.Evaluate(force: true);
    }

    private void RefreshTray()
    {
        if (_controller == null || _tray == null) return;
        var vr = _controller.VrMode;
        _status!.Text = (vr ? "VR mode" : "Desktop mode") + (_controller.Config.Override == ModeOverride.Auto ? " (auto)" : " (manual)");
        _tray.Text = vr ? "VR Audio Switcher - VR" : "VR Audio Switcher - Desktop";
        var old = _tray.Icon; _tray.Icon = MakeIcon(vr); old?.Dispose();
    }

    private void ShowWindow()
    {
        if (_window == null)
        {
            _window = new MainWindow(_controller!);
            _window.Closed += (_, _) => { _window = null; Trim(); };
        }
        _window.Show();
        _window.Activate();
    }

    private void Quit()
    {
        _tray!.Visible = false; _tray.Dispose();
        _controller?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }

    private static Icon MakeIcon(bool vr)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(vr ? Color.FromArgb(124, 92, 255) : Color.FromArgb(0, 168, 120));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 3);
            g.DrawArc(pen, 7, 6, 18, 18, 180, 180);
            using var w = new SolidBrush(Color.White);
            g.FillRectangle(w, 6, 15, 5, 9);
            g.FillRectangle(w, 21, 15, 5, 9);
        }
        var h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        return (Icon)tmp.Clone();
    }
}
