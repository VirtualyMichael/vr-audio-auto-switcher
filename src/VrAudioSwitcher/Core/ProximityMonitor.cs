using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Valve.VR;

namespace VrAudioSwitcher.Core;

/// <summary>
/// Reads the headset's "worn" state from SteamVR (the proximity sensor feeds the HMD activity level).
/// Only connects to OpenVR while SteamVR is running and the option is on; polls once a second.
/// "Worn" = activity level UserInteraction. Off-head must last <c>OffDelaySeconds</c> before it counts,
/// so adjusting the strap doesn't flip the audio; putting it back on is instant.
/// </summary>
public sealed class ProximityMonitor : IDisposable
{
    private readonly AppConfig _cfg;
    private System.Threading.Timer? _timer;
    private bool _inited;
    private DateTime? _offSince;
    private readonly object _lock = new();

    public bool Worn { get; private set; } = true;
    public string Status { get; private set; } = "Off";
    public event Action? Changed;

    public ProximityMonitor(AppConfig cfg) => _cfg = cfg;

    public void Start()
    {
        lock (_lock)
        {
            if (_timer != null) return;
            Status = "Connecting to SteamVR...";
            _timer = new System.Threading.Timer(_ => Tick(), null, 0, 1000);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose(); _timer = null;
            ShutdownOpenVr();
            _offSince = null;
            var wasWorn = Worn;
            Worn = true;
            Status = "Off";
            if (!wasWorn) Changed?.Invoke();
        }
    }

    private void Tick()
    {
        bool? worn = null;
        lock (_lock)
        {
            if (_timer == null) return;
            try
            {
                if (!_inited && !TryInit()) return;

                var ev = new VREvent_t();
                var size = (uint)Marshal.SizeOf<VREvent_t>();
                while (OpenVR.System != null && OpenVR.System.PollNextEvent(ref ev, size))
                {
                    if (ev.eventType == (uint)EVREventType.VREvent_Quit)
                    {
                        OpenVR.System.AcknowledgeQuit_Exiting();
                        ShutdownOpenVr();
                        Status = "SteamVR closing";
                        return;
                    }
                }

                var level = OpenVR.System!.GetTrackedDeviceActivityLevel(OpenVR.k_unTrackedDeviceIndex_Hmd);
                var isWorn = level == EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction;
                Status = isWorn ? "Headset on" : level switch
                {
                    EDeviceActivityLevel.k_EDeviceActivityLevel_Unknown => "Headset not reporting",
                    EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction_Timeout => "Headset off (just removed)",
                    _ => "Headset off"
                };

                if (isWorn) { _offSince = null; if (!Worn) { Worn = true; worn = true; } }
                else
                {
                    _offSince ??= DateTime.UtcNow;
                    if (Worn && (DateTime.UtcNow - _offSince.Value).TotalSeconds >= _cfg.ProximityOffDelaySeconds)
                    { Worn = false; worn = false; }
                }
            }
            catch (Exception ex) { Status = "Error: " + ex.Message; ShutdownOpenVr(); }
        }
        if (worn != null) Changed?.Invoke();
    }

    private bool TryInit()
    {
        EnsureNative();
        var err = EVRInitError.None;
        // Background = attach to a running SteamVR without launching it or loading drivers.
        OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
        if (err != EVRInitError.None)
        {
            Status = "Waiting for SteamVR (" + err + ")";
            return false;
        }
        _inited = true;
        return true;
    }

    private void ShutdownOpenVr()
    {
        if (!_inited) return;
        try { OpenVR.Shutdown(); } catch { }
        _inited = false;
    }

    // ---- native openvr_api.dll is embedded; extract on first use so the exe stays a single file ----
    private static bool _nativeReady;
    private static void EnsureNative()
    {
        if (_nativeReady) return;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrAudioSwitcher", "native");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "openvr_api.dll");
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VrAudioSwitcher.openvr_api.dll")!)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != s.Length)
            {
                try { using var f = File.Create(path); s.CopyTo(f); } catch (IOException) { /* in use by another instance: fine */ }
            }
        }
        var handle = NativeLibrary.Load(path);
        NativeLibrary.SetDllImportResolver(typeof(ProximityMonitor).Assembly,
            (name, _, _) => name == "openvr_api" ? handle : IntPtr.Zero);
        _nativeReady = true;
    }

    public void Dispose() => Stop();
}
