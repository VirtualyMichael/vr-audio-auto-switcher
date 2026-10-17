using System.Runtime.InteropServices;

namespace VrAudioSwitcher.Audio;

public record AudioDevice(string Id, string Name, bool IsOutput);

public static class AudioService
{
    private const int DeviceStateActive = 1;
    private static PropertyKey FriendlyNameKey = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14
    };

    public static List<AudioDevice> GetDevices(bool output)
    {
        var list = new List<AudioDevice>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.EnumAudioEndpoints(output ? EDataFlow.Render : EDataFlow.Capture, DeviceStateActive, out var col) != 0) return list;
            col.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                col.Item(i, out var dev);
                dev.GetId(out var id);
                list.Add(new AudioDevice(id, ReadName(dev), output));
                Marshal.ReleaseComObject(dev);
            }
            Marshal.ReleaseComObject(col);
        }
        finally { Marshal.ReleaseComObject(enumerator); }
        return list;
    }

    public static string? GetDefaultId(bool output, bool communications = false)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            var hr = enumerator.GetDefaultAudioEndpoint(output ? EDataFlow.Render : EDataFlow.Capture,
                communications ? ERole.Communications : ERole.Multimedia, out var dev);
            if (hr != 0) return null;
            dev.GetId(out var id);
            Marshal.ReleaseComObject(dev);
            return id;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    public static bool IsActive(string id)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.GetDevice(id, out var dev) != 0) return false;
            dev.GetState(out var state);
            Marshal.ReleaseComObject(dev);
            return state == DeviceStateActive;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    public static string? GetName(string id)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.GetDevice(id, out var dev) != 0) return null;
            var n = ReadName(dev);
            Marshal.ReleaseComObject(dev);
            return n;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    /// <summary>Sets the device as default for all three roles (Console, Multimedia, Communications).</summary>
    public static bool SetDefault(string id)
    {
        if (string.IsNullOrEmpty(id) || !IsActive(id)) return false;
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            var ok = true;
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                ok &= policy.SetDefaultEndpoint(id, role) == 0;
            return ok;
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    private static string ReadName(IMMDevice dev)
    {
        if (dev.OpenPropertyStore(0, out var store) != 0) return "Unknown device";
        try
        {
            store.GetValue(ref FriendlyNameKey, out var pv);
            var name = pv.vt == 31 ? Marshal.PtrToStringUni(pv.ptr) : null;
            NativeMethods.PropVariantClear(ref pv);
            return name ?? "Unknown device";
        }
        finally { Marshal.ReleaseComObject(store); }
    }
}
