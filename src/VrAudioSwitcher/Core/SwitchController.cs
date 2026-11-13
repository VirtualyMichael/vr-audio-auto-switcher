using VrAudioSwitcher.Audio;

namespace VrAudioSwitcher.Core;

public sealed class SwitchController : IDisposable
{
    public AppConfig Config { get; }
    public VrDetector Detector { get; } = new();
    public DiscordBridge Bridge { get; }
    public DiscordRpcClient Rpc { get; }
    public ProximityMonitor Proximity { get; }
    public bool VrMode { get; private set; }
    public event Action? ModeChanged;
    private readonly object _lock = new();

    public SwitchController(AppConfig config)
    {
        Config = config;
        Bridge = new DiscordBridge(config.BridgeToken);
        Bridge.Start();
        Rpc = new DiscordRpcClient(config);
        Proximity = new ProximityMonitor(config);
        Proximity.Changed += () => Evaluate();
        if (config.Discord == DiscordMode.Rpc) Rpc.Start();
        Detector.Changed += _ => Evaluate();
        Evaluate();
    }

    public bool EffectiveVr => Config.Override switch
    {
        ModeOverride.Vr => true,
        ModeOverride.Desktop => false,
        _ => Detector.VrActive && (!Config.ProximitySwitching || Proximity.Worn)
    };

    public void Evaluate(bool force = false)
    {
        lock (_lock)
        {
            SyncProximity();
            var vr = EffectiveVr;
            if (!force && vr == VrMode && _applied) return;
            VrMode = vr;
            Apply(vr ? Config.Vr : Config.Desktop);
            _applied = true;
        }
        ModeChanged?.Invoke();
    }
    private bool _applied;

    // Only talk to OpenVR while it can matter: option on, SteamVR up, no manual override.
    private void SyncProximity()
    {
        if (Config.ProximitySwitching && Config.Override == ModeOverride.Auto && Detector.VrActive) Proximity.Start();
        else Proximity.Stop();
    }

    private void Apply(Profile p)
    {
        var changed = false;
        if (p.OutputId != null && AudioService.GetDefaultId(true) != p.OutputId) changed |= AudioService.SetDefault(p.OutputId);
        if (p.InputId != null && AudioService.GetDefaultId(false) != p.InputId) changed |= AudioService.SetDefault(p.InputId);

        if (Config.Discord == DiscordMode.Rpc) { Rpc.Start(); Rpc.SetDesired(p); }
        else if (Config.Discord == DiscordMode.BetterDiscord) Bridge.Push(p);
        else if (changed && Config.RestartDiscordIfNeeded) DiscordBridge.RestartDiscord();
    }

    public void Dispose() { Detector.Dispose(); Bridge.Dispose(); Rpc.Dispose(); Proximity.Dispose(); }
}
