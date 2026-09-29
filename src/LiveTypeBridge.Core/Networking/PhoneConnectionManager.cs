using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Protocol;
using Microsoft.AspNetCore.Http;

namespace LiveTypeBridge.Core.Networking;

/// <summary>
/// Per-socket state: which session it belongs to, last activity, and an async send
/// gate so heartbeats and acks never interleave inside one WebSocket frame sequence.
/// </summary>
internal sealed class SocketState
{
    public required Guid Id { get; init; }
    public required WebSocket Ws { get; init; }
    public string Remote { get; init; } = "?";
    public PairingSession? Session { get; set; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public SemaphoreSlim SendGate { get; } = new(1, 1);
    public CancellationTokenSource Cts { get; } = new();
    public volatile bool Closed;
}

/// <summary>
/// The WebSocket conversation layer: pairing handshake, one-phone-per-session
/// enforcement, heartbeat/stale watchdog, and routing of text ops into the input pump.
/// </summary>
public sealed class PhoneConnectionManager : IDisposable
{
    public const int HeartbeatIntervalMs = 2000;
    public const int StaleTimeoutMs = 12_000;
    private const int MaxMessageBytes = 128 * 1024;

    private readonly PairingSessionManager _sessions;
    private readonly Func<PairingSession, PumpCore> _pumpFactory;
    private readonly ConcurrentDictionary<Guid, SocketState> _sockets = new();
    private readonly ConcurrentDictionary<string, PumpCore> _pumps = new(); // sessionId -> pump
    private readonly Timer _watchdog;
    private volatile bool _disposed;

    public bool HasPairedPhone { get; private set; }
    public string? PairedDeviceName { get; private set; }
    public DateTime? LastReceiveUtc { get; private set; }

    public event Action<string /*deviceName*/, string /*sessionId*/>? PhonePaired;
    public event Action<string /*reason*/>? PhoneDisconnected;
    public event Action<double /*lagMs*/, int? /*rttMs*/>? LatencySample;
    public event Action<string /*code*/, string /*detail*/>? ProtocolError;

    public PhoneConnectionManager(PairingSessionManager sessions, Func<PairingSession, PumpCore> pumpFactory)
    {
        _sessions = sessions;
        _pumpFactory = pumpFactory;
        _sessions.SessionInvalidated += OnSessionInvalidated;
        _watchdog = new Timer(_ => WatchdogTick(), null, 3000, 3000);
    }

    public PumpCore? TryGetPump(string sessionId) => _pumps.TryGetValue(sessionId, out var p) ? p : null;

    // ---- Socket lifecycle -------------------------------------------------

    public async Task HandleSocketAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest || _disposed) return;

        WebSocket ws;
        try { ws = await ctx.WebSockets.AcceptWebSocketAsync(); }
        catch (WebSocketException) { return; }

        var st = new SocketState
        {
            Id = Guid.NewGuid(),
            Ws = ws,
            Remote = ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        };
        _sockets[st.Id] = st;

        try
        {
            var first = await ReceiveAsync(st, TimeSpan.FromSeconds(6));
            if (first is null) return;
            if (first.Type != Envelope.Types.PairRequest)
            {
                await SendErrorAndCloseAsync(st, Envelope.Errors.UnexpectedMessage, "pair_request expected");
                return;
            }

            var session = await TryPairAsync(st, first);
            if (session is null) return;

            HasPairedPhone = true;
            PairedDeviceName = session.PairedDeviceName;
            PhonePaired?.Invoke(session.PairedDeviceName ?? "?", session.SessionId);

            while (!_disposed && !st.Closed)
            {
                var env = await ReceiveAsync(st, Timeout.InfiniteTimeSpan);
                if (env is null) return;
                if (!await HandlePairedMessageAsync(st, session, env)) return;
            }
        }
        catch (OperationCanceledException) { /* watchdog or shutdown */ }
        catch (WebSocketException) { /* phone dropped */ }
        catch (Exception)
        {
            // A malformed client must never crash the host; the finally block cleans up.
        }
        finally
        {
            await CleanupSocketAsync(st);
        }
    }

    private async Task<PairingSession?> TryPairAsync(SocketState st, Envelope pairRequest)
    {
        var session = _sessions.GetActive(pairRequest.SessionId);
        if (session is null)
        {
            await SendErrorAndCloseAsync(st, Envelope.Errors.UnknownSession, "no such session");
            return null;
        }
        if (!session.VerifyPin(pairRequest.Pin))
        {
            ProtocolError?.Invoke(Envelope.Errors.BadPin, $"from {st.Remote}");
            await SendErrorAndCloseAsync(st, Envelope.Errors.BadPin, "PIN mismatch");
            return null;
        }
        if (session.BoundSocket is SocketState bound && !bound.Closed && !ReferenceEquals(bound, st))
        {
            await SendErrorAndCloseAsync(st, Envelope.Errors.AlreadyPaired, "another phone holds this session");
            return null;
        }

        var name = string.IsNullOrWhiteSpace(pairRequest.DeviceName) ? "Android" : pairRequest.DeviceName.Trim();
        session.MarkPaired(name.Length > 40 ? name[..40] : name);
        session.BoundSocket = st;
        st.Session = session;

        var pump = _pumpFactory(session);
        _pumps[session.SessionId] = pump;

        await SendToSocketAsync(st, new Envelope(
            Type: Envelope.Types.SessionReady,
            Ok: true,
            Token: session.IssueAuthToken(),
            HeartbeatIntervalMs: HeartbeatIntervalMs,
            ServerTimeUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        return session;
    }

    private async Task<bool> HandlePairedMessageAsync(SocketState st, PairingSession session, Envelope env)
    {
        st.LastSeenUtc = DateTime.UtcNow;
        LastReceiveUtc = st.LastSeenUtc;

        if (env.Type != Envelope.Types.Heartbeat && !session.VerifyToken(env.Token))
        {
            ProtocolError?.Invoke(Envelope.Errors.BadToken, $"type={env.Type}");
            await SendErrorAndCloseAsync(st, Envelope.Errors.BadToken, "invalid or missing token");
            return false;
        }

        switch (env.Type)
        {
            case Envelope.Types.Heartbeat:
                if (env.Timestamp is long t)
                {
                    var lag = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t);
                    LatencySample?.Invoke(lag, env.LastRttMs);
                }
                await SendToSocketAsync(st, new Envelope(
                    Type: Envelope.Types.HeartbeatAck,
                    Timestamp: env.Timestamp,
                    ServerTimeUtc: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                return true;

            case Envelope.Types.TextEdit:
                if (_pumps.TryGetValue(session.SessionId, out var pump))
                    pump.HandleEdit(env);
                return true;

            case Envelope.Types.ResetStream:
                if (_pumps.TryGetValue(session.SessionId, out var pump2))
                    pump2.HandleReset(env);
                return true;

            case Envelope.Types.Disconnect:
                await CloseSocketAsync(st, env.Reason ?? Envelope.Reasons.PhoneClosed, notifyPhone: false);
                return false;

            default:
                ProtocolError?.Invoke(Envelope.Errors.UnexpectedMessage, $"type={env.Type}");
                await SendErrorAndCloseAsync(st, Envelope.Errors.UnexpectedMessage, "not a client message");
                return false;
        }
    }

    /// <summary>User clicked "قطع الاتصال": kill the socket and burn the session.</summary>
    public async Task DisconnectAllAsync(string reason = Envelope.Reasons.UserDisconnect)
    {
        foreach (var st in _sockets.Values.Where(s => s.Session is not null).ToList())
        {
            await CloseSocketAsync(st, reason, notifyPhone: true);
        }
        _sessions.InvalidateActiveUserSession();
    }

    /// <summary>Sends a protocol envelope to the phone bound to the given session (ordered).</summary>
    public Task SendToSessionAsync(PairingSession session, Envelope env)
    {
        var st = session.BoundSocket as SocketState;
        return st is null ? Task.CompletedTask : SendToSocketAsync(st, env);
    }

    private async Task<Envelope?> ReceiveAsync(SocketState st, TimeSpan timeout)
    {
        CancellationTokenSource? timeoutCts = null;
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(st.Cts.Token);
            timeoutCts.CancelAfter(timeout);
        }
        var token = timeoutCts?.Token ?? st.Cts.Token;

        var buffer = new byte[8192];
        using var message = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await st.Ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            }
            catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
            {
                await CloseSocketAsync(st, Envelope.Reasons.Stale, notifyPhone: false);
                return null;
            }

            st.LastSeenUtc = DateTime.UtcNow;

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseSocketAsync(st, Envelope.Reasons.PhoneClosed, notifyPhone: false);
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > MaxMessageBytes)
            {
                await SendErrorAndCloseAsync(st, Envelope.Errors.TooLarge, "frame over 128KB");
                return null;
            }
            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(message.ToArray());
            message.SetLength(0);
            if (!ProtocolJson.TryParse(json, out var env))
            {
                await SendErrorAndCloseAsync(st, Envelope.Errors.Malformed, "bad JSON envelope");
                return null;
            }
            return env;
        }
    }

    private async Task SendToSocketAsync(SocketState st, Envelope env)
    {
        if (st.Closed || st.Ws.State != WebSocketState.Open) return;
        var acquired = await st.SendGate.WaitAsync(TimeSpan.FromSeconds(5));
        if (!acquired) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(env));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(st.Cts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await st.Ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        catch { /* socket dying; the reader/watchdog will clean up */ }
        finally
        {
            st.SendGate.Release();
        }
    }

    private async Task SendErrorAndCloseAsync(SocketState st, string code, string message)
    {
        ProtocolError?.Invoke(code, message);
        await SendToSocketAsync(st, new Envelope(Type: Envelope.Types.Error, ErrorCode: code, ErrorMessage: message));
        await CloseSocketAsync(st, code, notifyPhone: false);
    }

    internal async Task CloseSocketAsync(SocketState st, string reason, bool notifyPhone)
    {
        if (st.Closed) return;
        st.Closed = true;

        if (notifyPhone && st.Ws.State == WebSocketState.Open)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(
                    new Envelope(Type: Envelope.Types.Disconnect, Reason: reason)));
                await st.Ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
            }
            catch { /* already gone */ }
        }

        var session = st.Session;
        if (session?.BoundSocket is SocketState bound && ReferenceEquals(bound, st))
            session.BoundSocket = null;

        try { st.Cts.Cancel(); } catch { }
        try
        {
            if (st.Ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await st.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
        }
        catch { }
        try { st.Ws.Dispose(); } catch { }
    }

    private async Task CleanupSocketAsync(SocketState st)
    {
        if (!st.Closed) await CloseSocketAsync(st, "cleanup", notifyPhone: false);
        _sockets.TryRemove(st.Id, out _);

        var session = st.Session;
        if (session is not null && _pumps.TryRemove(session.SessionId, out var pump))
            pump.Dispose();

        if (HasPairedPhone && session is not null && !session.IsSelfTest)
        {
            HasPairedPhone = false;
            PairedDeviceName = null;
            PhoneDisconnected?.Invoke("phone_gone");
        }
    }

    private void OnSessionInvalidated(PairingSession session)
    {
        foreach (var st in _sockets.Values.Where(s => ReferenceEquals(s.Session, session)).ToList())
        {
            // Session was replaced or expired: cut the old phone off immediately.
            _ = CloseSocketAsync(st, Envelope.Reasons.Replaced, notifyPhone: false);
        }
    }

    private void WatchdogTick()
    {
        if (_disposed) return;
        var cutoff = DateTime.UtcNow.AddMilliseconds(-StaleTimeoutMs);
        foreach (var st in _sockets.Values.Where(s => s.Session is not null && s.LastSeenUtc < cutoff).ToList())
        {
            _ = CloseSocketAsync(st, Envelope.Reasons.Stale, notifyPhone: false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchdog.Dispose();
        foreach (var st in _sockets.Values.ToList())
            _ = CloseSocketAsync(st, "shutdown", notifyPhone: false);
        foreach (var pump in _pumps.Values.ToList()) pump.Dispose();
        _pumps.Clear();
    }
}
