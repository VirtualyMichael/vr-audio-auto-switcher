using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace VrAudioSwitcher.Core;

/// <summary>
/// Loopback WebSocket server the BetterDiscord plugin connects to. Pushes the desired
/// Discord input/output device (by name + id). Token-protected, 127.0.0.1 only.
/// </summary>
public sealed class DiscordBridge : IDisposable
{
    public const int Port = 48721;
    private readonly string _token;
    private HttpListener? _listener;
    private readonly ConcurrentDictionary<WebSocket, byte> _clients = new();
    private string? _last;
    public int ClientCount => _clients.Count;

    public DiscordBridge(string token) => _token = token;

    public void Start()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(AcceptLoop);
        }
        catch { _listener = null; }
    }

    public void Stop()
    {
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoop()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { return; }
            if (!ctx.Request.IsWebSocketRequest || ctx.Request.QueryString["token"] != _token)
            {
                ctx.Response.StatusCode = 403; ctx.Response.Close(); continue;
            }
            _ = Task.Run(() => Serve(ctx));
        }
    }

    private async Task Serve(HttpListenerContext ctx)
    {
        WebSocket ws;
        try { ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket; } catch { return; }
        _clients[ws] = 0;
        try
        {
            if (_last != null) await Send(ws, _last);
            var buf = new byte[256];
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { }
        finally { _clients.TryRemove(ws, out _); ws.Dispose(); }
    }

    public void Push(Profile p)
    {
        _last = JsonSerializer.Serialize(new
        {
            type = "devices",
            outputName = p.OutputName,
            inputName = p.InputName,
            outputId = p.OutputId,
            inputId = p.InputId
        });
        foreach (var c in _clients.Keys) _ = Send(c, _last);
    }

    private static async Task Send(WebSocket ws, string msg)
    {
        try { if (ws.State == WebSocketState.Open) await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { }
    }

    public void Dispose() => Stop();

    // --- stock-Discord fallback: restart so it re-reads the "Default" device ---
    public static void RestartDiscord()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
        var update = Path.Combine(dir, "Update.exe");
        if (!File.Exists(update)) return;
        var running = Process.GetProcessesByName("Discord");
        if (running.Length == 0) return; // only restart if it was already open
        foreach (var p in running) { try { p.Kill(); } catch { } p.Dispose(); }
        Process.Start(new ProcessStartInfo(update, "--processStart Discord.exe") { UseShellExecute = false });
    }
}
