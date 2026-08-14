using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DgLabSocketSpire2.Configuration;

namespace DgLabSocketSpire2.Bridge;

internal sealed class DgLabV4Client : IDgLabClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly BridgeService _service;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private string? _clientId;
    private string? _targetId;
    private bool _isAttached;
    private int _strengthA;
    private int _strengthB;
    private int _limitA;
    private int _limitB;
    private string _lastFeedback = string.Empty;
    private string _lastError = string.Empty;
    private string _lastNotice = string.Empty;

    public DgLabV4Client(BridgeService service)
    {
        _service = service;
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public bool IsBound
    {
        get { lock (_stateGate) return _isAttached; }
    }

    public string? ClientId
    {
        get { lock (_stateGate) return _clientId; }
    }

    public string? TargetId
    {
        get { lock (_stateGate) return _targetId; }
    }

    public int StrengthA
    {
        get { lock (_stateGate) return _strengthA; }
    }

    public int StrengthB
    {
        get { lock (_stateGate) return _strengthB; }
    }

    public int LimitA
    {
        get { lock (_stateGate) return _limitA; }
    }

    public int LimitB
    {
        get { lock (_stateGate) return _limitB; }
    }

    public string LastFeedback
    {
        get { lock (_stateGate) return _lastFeedback; }
    }

    public string LastError
    {
        get { lock (_stateGate) return _lastError; }
    }

    public string LastNotice
    {
        get { lock (_stateGate) return _lastNotice; }
    }

    public void Start(int port)
    {
        if (_loopTask != null) return;
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(port, _loopCts.Token));
    }

    public async Task SendStrengthAsync(ChannelRef channel, StrengthOperation operation, int value)
    {
        var targetId = TargetId;
        if (!IsConnected || !IsBound || string.IsNullOrWhiteSpace(targetId))
        {
            ModLog.Warn($"[V4] Skipped SendStrength: connected={IsConnected}, bound={IsBound}, targetId={targetId}");
            return;
        }

        var channelName = channel == ChannelRef.A ? "A" : "B";
        var clamped = Math.Clamp(value, 0, 200);

        if (operation == StrengthOperation.Clear)
        {
            await SendMessageAsync(targetId, new { op = "clear", channel = channelName });
        }
        else
        {
            await SendMessageAsync(targetId, new { op = "set-strength", channel = channelName, value = clamped });
        }

        ModLog.Info($"[V4] Sent strength: channel={channelName}, op={operation}, value={clamped}");
    }

    public async Task SendWaveAsync(ChannelRef channel, string[] frames, int durationSeconds)
    {
        var targetId = TargetId;
        if (!IsConnected || !IsBound || string.IsNullOrWhiteSpace(targetId))
        {
            ModLog.Warn($"[V4] Skipped SendWave: connected={IsConnected}, bound={IsBound}, targetId={targetId}");
            return;
        }

        var channelName = channel == ChannelRef.A ? "A" : "B";
        await SendMessageAsync(targetId, new
        {
            op = "pulse",
            channel = channelName,
            time = Math.Max(1, durationSeconds),
            data = frames
        });

        ModLog.Info($"[V4] Sent wave: channel={channelName}, duration={durationSeconds}, frames={frames.Length}");
    }

    public async Task ClearChannelAsync(ChannelRef channel)
    {
        var targetId = TargetId;
        if (!IsConnected || !IsBound || string.IsNullOrWhiteSpace(targetId))
        {
            ModLog.Warn($"[V4] Skipped Clear: connected={IsConnected}, bound={IsBound}");
            return;
        }

        var channelName = channel == ChannelRef.A ? "A" : "B";
        await SendMessageAsync(targetId, new { op = "clear", channel = channelName });
        ModLog.Info($"[V4] Sent clear: channel={channelName}");
    }

    private async Task SendMessageAsync(string targetClientId, object data)
    {
        await SendJsonAsync(new
        {
            type = "message",
            clientId = targetClientId,
            data
        });
    }

    private async Task SendJsonAsync(object payload)
    {
        var socket = _socket;
        if (socket == null || socket.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync();
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ModLog.Warn($"[V4] Send failed: {ex.Message}");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunLoopAsync(int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                _socket = socket;
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ct);
                ModLog.Info("[V4] Connected to DG-LAB V4 server.");
                _service.NotifyFrontendConnectionChanged();
                await ReceiveLoopAsync(socket, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { ModLog.Warn($"[V4] Connection error: {ex.Message}"); }
            finally
            {
                _socket = null;
                lock (_stateGate)
                {
                    _isAttached = false;
                    _targetId = null;
                }
                _service.NotifyFrontendConnectionChanged();
            }

            try { await Task.Delay(1500, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "v4_close", ct);
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            HandleMessage(Encoding.UTF8.GetString(ms.ToArray()));
        }
    }

    private void HandleMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var te) ? te.GetString() ?? "" : "";

            switch (type)
            {
                case "hello":
                    lock (_stateGate)
                    {
                        _clientId = root.TryGetProperty("clientId", out var ce) ? ce.GetString() : null;
                        _lastNotice = "V4 connected.";
                    }
                    ModLog.Info($"[V4] Hello received, clientId={_clientId}");
                    break;

                case "client_attached":
                    lock (_stateGate)
                    {
                        _targetId = root.TryGetProperty("clientId", out var ce) ? ce.GetString() : null;
                        _isAttached = true;
                        _lastNotice = "V4 APP paired.";
                    }
                    ModLog.Info($"[V4] Client attached: targetId={_targetId}");
                    break;

                case "controller_disconnected":
                    lock (_stateGate)
                    {
                        _targetId = null;
                        _isAttached = false;
                        _lastNotice = "V4 controller disconnected.";
                    }
                    break;

                case "client_disconnected":
                    lock (_stateGate)
                    {
                        _targetId = null;
                        _isAttached = false;
                        _lastNotice = "V4 APP disconnected.";
                    }
                    break;

                case "message":
                    if (root.TryGetProperty("data", out var data))
                    {
                        ParseAppData(data);
                    }
                    break;

                case "error":
                    lock (_stateGate)
                    {
                        _lastError = root.TryGetProperty("message", out var me) ? me.GetString() ?? "unknown" : "unknown";
                    }
                    ModLog.Warn($"[V4] Error: {_lastError}");
                    break;
            }

            _service.NotifyFrontendMessageReceived();
        }
        catch (Exception ex)
        {
            ModLog.Warn($"[V4] Parse error: {ex.Message}");
        }
    }

    private void ParseAppData(JsonElement data)
    {
        var op = data.TryGetProperty("op", out var oe) ? oe.GetString() ?? "" : "";
        lock (_stateGate)
        {
            if (op == "report")
            {
                var channel = data.TryGetProperty("channel", out var ce) ? ce.GetString() ?? "" : "";
                var value = data.TryGetProperty("value", out var ve) && ve.TryGetInt32(out var v) ? v : 0;
                var limit = data.TryGetProperty("limit", out var le) && le.TryGetInt32(out var l) ? l : 0;

                if (channel.Equals("A", StringComparison.OrdinalIgnoreCase))
                {
                    _strengthA = value;
                    _limitA = limit;
                }
                else if (channel.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    _strengthB = value;
                    _limitB = limit;
                }
            }
            else if (op == "feedback")
            {
                _lastFeedback = data.ToString();
            }
            else
            {
                _lastNotice = data.ToString();
            }
        }
    }

    public void Dispose()
    {
        _loopCts?.Cancel();
        try { _loopTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _socket?.Dispose();
        _sendLock.Dispose();
        _loopCts?.Dispose();
    }
}
