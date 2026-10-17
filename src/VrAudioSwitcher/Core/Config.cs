using System.IO;
using System.Text.Json;

namespace VrAudioSwitcher.Core;

public enum DiscordMode { WindowsDefault, BetterDiscord, Rpc }
public enum ModeOverride { Auto, Desktop, Vr }

public class Profile
{
    public string? OutputId { get; set; }
    public string? InputId { get; set; }
    public string? OutputName { get; set; }
    public string? InputName { get; set; }
}

public class AppConfig
{
    public Profile Desktop { get; set; } = new();
    public Profile Vr { get; set; } = new();
    public DiscordMode Discord { get; set; } = DiscordMode.WindowsDefault;
    public bool RestartDiscordIfNeeded { get; set; }
    public ModeOverride Override { get; set; } = ModeOverride.Auto;
    public string? DiscordClientId { get; set; }
    public string? DiscordClientSecret { get; set; }
    public string? RpcAccessToken { get; set; }
    public string? RpcRefreshToken { get; set; }
    public bool StartMinimized { get; set; } = true;
    public string BridgeToken { get; set; } = Guid.NewGuid().ToString("N");

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VrAudioSwitcher");
    private static readonly string FilePath = Path.Combine(Dir, "config.json");
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static AppConfig Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts)); }
        catch { }
    }
}
