using System.Diagnostics;
using System.Threading;

namespace VrAudioSwitcher.Core;

/// <summary>
/// Detects SteamVR (vrserver.exe) with a 3s timer. One cheap process-name lookup per tick.
/// Stop is debounced (needs 2 consecutive misses) so SteamVR restarts don't flip the audio.
/// </summary>
public sealed class VrDetector : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private int _misses;
    public bool VrActive { get; private set; }
    public event Action<bool>? Changed;

    public VrDetector() => _timer = new System.Threading.Timer(_ => Tick(), null, 0, 3000);

    private void Tick()
    {
        bool running;
        var procs = Process.GetProcessesByName("vrserver");
        running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();

        if (running) { _misses = 0; if (!VrActive) { VrActive = true; Changed?.Invoke(true); } }
        else if (VrActive && ++_misses >= 2) { VrActive = false; _misses = 0; Changed?.Invoke(false); }
    }

    public void Dispose() => _timer.Dispose();
}
