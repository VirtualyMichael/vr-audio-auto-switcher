using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VrAudioSwitcher.Core;

/// <summary>
/// Unmodded Discord control over the local RPC named pipe (discord-ipc-N).
/// Needs a Discord developer app (client id + secret, redirect URI http://127.0.0.1, you added as tester).
/// Discord resets RPC-set voice settings when we disconnect, so the connection is held open and re-applied on reconnect.
/// </summary>
public sealed class DiscordRpcClient : IDisposable
{
    private const string Redirect = "http://127.0.0.1";
    private readonly AppConfig _cfg;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
    private NamedPipeClientStream? _pipe;
    private volatile bool _ready;
    private Profile? _desired;
    private Task? _loop;

    public string Status { get; private set; } = "Off";
    public event Action? StatusChanged;

    public DiscordRpcClient(AppConfig cfg) => _cfg = cfg;

    public void Start()
    {
        if (_loop != null) return;
        _loop = Task.Run(RunLoop);
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _pipe?.Dispose(); } catch { }
    }

    public void SetDesired(Profile p)
    {
        _desired = p;
        if (_ready) _ = Task.Run(ApplyDesired);
    }

    private void SetStatus(string s) { Status = s; StatusChanged?.Invoke(); }

    private async Task RunLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_cfg.DiscordClientId) || string.IsNullOrWhiteSpace(_cfg.DiscordClientSecret))
                { SetStatus("Enter client ID and secret"); await Task.Delay(3000, _cts.Token); continue; }
                await Session();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            _ready = false;
            try { await Task.Delay(5000, _cts.Token); } catch { return; }
        }
    }

    private async Task Session()
    {
        _pipe = await OpenPipe() ?? throw new IOException("Discord is not running");
        SetStatus("Connecting...");
        await WriteFrame(0, new JsonObject { ["v"] = 1, ["client_id"] = _cfg.DiscordClientId });
        _ = Task.Run(ReadLoop);

        // READY arrives as a dispatch; AUTHENTICATE/AUTHORIZE can follow immediately.
        await Task.Delay(300, _cts.Token);

        var authed = false;
        if (!string.IsNullOrEmpty(_cfg.RpcAccessToken))
        {
            try { await Command("AUTHENTICATE", new JsonObject { ["access_token"] = _cfg.RpcAccessToken }); authed = true; }
            catch { authed = false; }
        }
        if (!authed && !string.IsNullOrEmpty(_cfg.RpcRefreshToken))
        {
            try { await Token("refresh_token", "refresh_token", _cfg.RpcRefreshToken); await Command("AUTHENTICATE", new JsonObject { ["access_token"] = _cfg.RpcAccessToken }); authed = true; }
            catch { authed = false; }
        }
        if (!authed)
        {
            SetStatus("Approve the prompt in Discord");
            var auth = await Command("AUTHORIZE", new JsonObject
            {
                ["client_id"] = _cfg.DiscordClientId,
                ["scopes"] = new JsonArray("rpc", "rpc.voice.write", "rpc.voice.read")
            }, timeoutMs: 120000);
            var code = auth?["code"]?.GetValue<string>() ?? throw new Exception("No auth code");
            await Token("authorization_code", "code", code);
            await Command("AUTHENTICATE", new JsonObject { ["access_token"] = _cfg.RpcAccessToken });
        }

        _ready = true;
        SetStatus("Connected");
        await ApplyDesired();
        // Hold the connection until the pipe drops.
        while (_pipe is { IsConnected: true } && !_cts.IsCancellationRequested) await Task.Delay(2000, _cts.Token);
        throw new IOException("Discord closed");
    }

    private async Task Token(string grant, string field, string value)
    {
        using var http = new HttpClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _cfg.DiscordClientId!,
            ["client_secret"] = _cfg.DiscordClientSecret!,
            ["grant_type"] = grant,
            [field] = value,
            ["redirect_uri"] = Redirect
        });
        var resp = await http.PostAsync("https://discord.com/api/oauth2/token", form, _cts.Token);
        var body = await resp.Content.ReadAsStringAsync(_cts.Token);
        if (!resp.IsSuccessStatusCode) throw new Exception("Token exchange failed (check client secret / redirect URI)");
        var j = JsonNode.Parse(body)!;
        _cfg.RpcAccessToken = j["access_token"]!.GetValue<string>();
        _cfg.RpcRefreshToken = j["refresh_token"]?.GetValue<string>();
        _cfg.Save();
    }

    private async Task ApplyDesired()
    {
        var p = _desired;
        if (p == null || !_ready) return;
        try
        {
            var settings = await Command("GET_VOICE_SETTINGS", new JsonObject());
            var args = new JsonObject();
            var inp = Match(settings?["input"]?["available_devices"], p.InputName);
            var outp = Match(settings?["output"]?["available_devices"], p.OutputName);
            if (inp != null) args["input"] = new JsonObject { ["device_id"] = inp };
            if (outp != null) args["output"] = new JsonObject { ["device_id"] = outp };
            if (args.Count > 0) await Command("SET_VOICE_SETTINGS", args);
        }
        catch (Exception ex) { SetStatus("Apply failed: " + ex.Message); }
    }

    private static string Norm(string? s) => string.Join(' ', (s ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? Match(JsonNode? devices, string? name)
    {
        if (devices is not JsonArray arr || string.IsNullOrEmpty(name)) return null;
        var n = Norm(name);
        string? partial = null;
        foreach (var d in arr)
        {
            var dn = Norm(d?["name"]?.GetValue<string>());
            var id = d?["id"]?.GetValue<string>();
            if (dn == n) return id;
            if (partial == null && dn.Length > 0 && (dn.Contains(n) || n.Contains(dn))) partial = id;
        }
        return partial;
    }

    // ---- IPC framing ----
    private async Task<NamedPipeClientStream?> OpenPipe()
    {
        for (var i = 0; i < 10; i++)
        {
            var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await p.ConnectAsync(300, _cts.Token); return p; }
            catch (OperationCanceledException) { throw; }
            catch { p.Dispose(); }
        }
        return null;
    }

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private async Task WriteFrame(int op, JsonNode payload)
    {
        var data = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var buf = new byte[8 + data.Length];
        BitConverter.GetBytes(op).CopyTo(buf, 0);
        BitConverter.GetBytes(data.Length).CopyTo(buf, 4);
        data.CopyTo(buf, 8);
        await _writeLock.WaitAsync(_cts.Token);
        try { await _pipe!.WriteAsync(buf, _cts.Token); await _pipe.FlushAsync(_cts.Token); }
        finally { _writeLock.Release(); }
    }

    private async Task<JsonNode?> Command(string cmd, JsonNode args, int timeoutMs = 10000)
    {
        var nonce = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;
        try
        {
            await WriteFrame(1, new JsonObject { ["cmd"] = cmd, ["args"] = args, ["nonce"] = nonce });
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, _cts.Token));
            if (done != tcs.Task) throw new TimeoutException($"{cmd} timed out");
            return await tcs.Task;
        }
        finally { _pending.TryRemove(nonce, out _); }
    }

    private async Task ReadLoop()
    {
        var header = new byte[8];
        try
        {
            while (_pipe is { IsConnected: true })
            {
                await ReadExactly(header, 8);
                var op = BitConverter.ToInt32(header, 0);
                var len = BitConverter.ToInt32(header, 4);
                var body = new byte[len];
                await ReadExactly(body, len);
                if (op == 2) break;
                if (op == 3) { await WriteFrame(4, JsonNode.Parse(Encoding.UTF8.GetString(body))!); continue; }
                var msg = JsonNode.Parse(Encoding.UTF8.GetString(body));
                var nonce = msg?["nonce"]?.GetValue<string>();
                if (nonce != null && _pending.TryGetValue(nonce, out var tcs))
                {
                    if (msg?["evt"]?.GetValue<string>() == "ERROR")
                        tcs.TrySetException(new Exception(msg["data"]?["message"]?.GetValue<string>() ?? "RPC error"));
                    else tcs.TrySetResult(msg?["data"]);
                }
            }
        }
        catch { }
        finally { foreach (var t in _pending.Values) t.TrySetException(new IOException("Pipe closed")); try { _pipe?.Dispose(); } catch { } }
    }

    private async Task ReadExactly(byte[] buf, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = await _pipe!.ReadAsync(buf.AsMemory(read, count - read), _cts.Token);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }

    public void Dispose() => Stop();
}
