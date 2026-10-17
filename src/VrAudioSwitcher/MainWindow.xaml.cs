using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using VrAudioSwitcher.Audio;
using VrAudioSwitcher.Core;
using Wpf.Ui.Controls;

namespace VrAudioSwitcher;

public partial class MainWindow : FluentWindow
{
    private readonly SwitchController _c;
    private bool _loading = true;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "VrAudioSwitcher";

    public MainWindow(SwitchController controller)
    {
        _c = controller;
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        Load();
        _c.ModeChanged += OnModeChanged;
        _c.Rpc.StatusChanged += OnModeChanged;
        Closed += (_, _) => { _c.ModeChanged -= OnModeChanged; _c.Rpc.StatusChanged -= OnModeChanged; };
        _loading = false;
    }

    private void Rpc_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        _c.Config.DiscordClientId = RpcId.Text.Trim();
        _c.Config.DiscordClientSecret = RpcSecret.Text.Trim();
        _c.Config.Save();
    }

    private void OnModeChanged() => Dispatcher.BeginInvoke(RefreshStatus);

    private void Load()
    {
        var outs = AudioService.GetDevices(true);
        var ins = AudioService.GetDevices(false);
        Fill(DeskOut, outs, _c.Config.Desktop.OutputId);
        Fill(VrOut, outs, _c.Config.Vr.OutputId);
        Fill(DeskIn, ins, _c.Config.Desktop.InputId);
        Fill(VrIn, ins, _c.Config.Vr.InputId);

        // first run: preselect current defaults for the desktop profile
        if (_c.Config.Desktop.OutputId == null) Select(DeskOut, AudioService.GetDefaultId(true));
        if (_c.Config.Desktop.InputId == null) Select(DeskIn, AudioService.GetDefaultId(false));

        RbAuto.IsChecked = _c.Config.Override == ModeOverride.Auto;
        RbDesktop.IsChecked = _c.Config.Override == ModeOverride.Desktop;
        RbVr.IsChecked = _c.Config.Override == ModeOverride.Vr;
        DcWindows.IsChecked = _c.Config.Discord == DiscordMode.WindowsDefault;
        DcBd.IsChecked = _c.Config.Discord == DiscordMode.BetterDiscord;
        DcRpc.IsChecked = _c.Config.Discord == DiscordMode.Rpc;
        RpcId.Text = _c.Config.DiscordClientId ?? "";
        RpcSecret.Text = _c.Config.DiscordClientSecret ?? "";
        DcRestart.IsChecked = _c.Config.RestartDiscordIfNeeded;
        AutoStart.IsChecked = Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(RunName) != null;
        StartMin.IsChecked = _c.Config.StartMinimized;
        RefreshStatus();
    }

    private static void Fill(System.Windows.Controls.ComboBox box, List<AudioDevice> devices, string? selectedId)
    {
        box.ItemsSource = devices;
        Select(box, selectedId);
    }

    private static void Select(System.Windows.Controls.ComboBox box, string? id)
    {
        if (id == null) return;
        box.SelectedItem = (box.ItemsSource as List<AudioDevice>)?.FirstOrDefault(d => d.Id == id);
    }

    private void RefreshStatus()
    {
        var vr = _c.VrMode;
        ModeText.Text = vr ? "VR mode" : "Desktop mode";
        ModeSub.Text = _c.Config.Override != ModeOverride.Auto ? "Manual override"
            : _c.Detector.VrActive ? "SteamVR is running" : "Waiting for SteamVR";
        StatusCard.Background = new SolidColorBrush(vr ? System.Windows.Media.Color.FromArgb(0x33, 0x7C, 0x5C, 0xFF) : System.Windows.Media.Color.FromArgb(0x22, 0x00, 0xA8, 0x78));
        RpcStatus.Text = "Status: " + _c.Rpc.Status;
        BdStatus.Text = _c.Bridge.ClientCount > 0 ? "Plugin connected" : (IsBdInstalled() ? "Installed - waiting for Discord" : "Not installed");
    }

    private void Profile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Capture(_c.Config.Desktop, DeskOut, DeskIn);
        Capture(_c.Config.Vr, VrOut, VrIn);
        _c.Config.Save();
        _c.Evaluate(force: true);
    }

    private static void Capture(Profile p, System.Windows.Controls.ComboBox o, System.Windows.Controls.ComboBox i)
    {
        if (o.SelectedItem is AudioDevice od) { p.OutputId = od.Id; p.OutputName = od.Name; }
        if (i.SelectedItem is AudioDevice id) { p.InputId = id.Id; p.InputName = id.Name; }
    }

    private void Override_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _c.Config.Override = RbDesktop.IsChecked == true ? ModeOverride.Desktop : RbVr.IsChecked == true ? ModeOverride.Vr : ModeOverride.Auto;
        _c.Config.Save();
        _c.Evaluate(force: true);
    }

    private void Discord_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _c.Config.Discord = DcRpc.IsChecked == true ? DiscordMode.Rpc : DcBd.IsChecked == true ? DiscordMode.BetterDiscord : DiscordMode.WindowsDefault;
        _c.Config.RestartDiscordIfNeeded = DcRestart.IsChecked == true;
        _c.Config.Save();
        _c.Evaluate(force: true);
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (AutoStart.IsChecked == true) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunName, false);
    }

    private void StartMin_Click(object sender, RoutedEventArgs e)
    {
        _c.Config.StartMinimized = StartMin.IsChecked == true;
        _c.Config.Save();
    }

    private static string BdPluginDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterDiscord", "plugins");
    private static string BdPluginFile => Path.Combine(BdPluginDir, "VrAudioSwitcher.plugin.js");
    private static bool IsBdInstalled() => File.Exists(BdPluginFile);

    private void InstallBd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(BdPluginDir)) { BdStatus.Text = "BetterDiscord not found"; return; }
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VrAudioSwitcher.plugin.js")!;
            using var r = new StreamReader(s);
            var js = r.ReadToEnd().Replace("__TOKEN__", _c.Config.BridgeToken).Replace("__PORT__", DiscordBridge.Port.ToString());
            File.WriteAllText(BdPluginFile, js);
            BdStatus.Text = "Installed - enable it in Discord > Plugins";
        }
        catch (Exception ex) { BdStatus.Text = ex.Message; }
    }
}
