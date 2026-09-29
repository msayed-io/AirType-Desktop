using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Networking;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Protocol;

namespace LiveTypeBridge.Core.Diagnostics;

/// <summary>Injector used by the built-in self-test: records ops instead of typing.</summary>
public sealed class RecordingInjector : ITextInjector
{
    private readonly object _gate = new();

    public List<(int Backspaces, string Text)> Ops { get; } = new();

    public bool Inject(int backspaceCount, string text, out string? warningCode)
    {
        warningCode = null;
        lock (_gate) Ops.Add((backspaceCount, text));
        return true;
    }
}

public sealed record SelfTestResult(bool Passed, List<string> Lines, TimeSpan Duration);

/// <summary>
/// Full local pipeline check: spins up (or reuses) the WebSocket server, then acts like
/// a phone — pairs with a wrong PIN (must fail), pairs correctly, streams ordered edits,
/// a duplicate (must be ignored), a revision gap (must trigger reset_stream), performs
/// the resync handshake, heartbeats, and disconnects. Nothing is typed into real apps.
/// </summary>
public sealed class SelfTestRunner
{
    private static readonly ConcurrentDictionary<string, RecordingInjector> Recorders = new();

    /// <summary>Factory hook: self-test sessions get a recording injector instead of SendInput.</summary>
    public static ITextInjector GetOrCreateRecorder(string sessionId)
        => Recorders.GetOrAdd(sessionId, _ => new RecordingInjector());

    private readonly PhoneLinkServer _server;
    private readonly PairingSessionManager _sessions;
    private readonly PhoneConnectionManager _connections;

    public SelfTestRunner(PhoneLinkServer server, PairingSessionManager sessions, PhoneConnectionManager connections)
    {
        _server = server;
        _sessions = sessions;
        _connections = connections;
    }

    public async Task<SelfTestResult> RunAsync()
    {
        var sw = Stopwatch.StartNew();
        var lines = new List<string>();
        var pass = true;

        bool Check(string name, bool ok, string detail = "")
        {
            lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}");
            if (!ok) pass = false;
            return ok;
        }

        var userSession = _sessions.ActiveUserSession;
        if (userSession is { IsPaired: true })
        {
            Check("preflight", false, "an active phone pairing exists — disconnect it first");
            return new SelfTestResult(false, lines, sw.Elapsed);
        }

        var startedHere = false;
        if (!_server.IsRunning)
        {
            startedHere = await _server.StartAsync(55017, _connections, loopbackOnly: true);
            Check("server start (loopback)", startedHere, startedHere ? $"port {_server.ActualPort}" : "no free port");
            if (!startedHere) return new SelfTestResult(false, lines, sw.Elapsed);
        }

        var url = $"ws://127.0.0.1:{_server.ActualPort}/livetype";
        lines.Add($"INFO  target {url}");

        var session = _sessions.Create(TimeSpan.FromMinutes(5), selfTest: true);
        var recorder = (RecordingInjector)GetOrCreateRecorder(session.SessionId);

        double lagTotal = 0;
        var lagCount = 0;

        try
        {
            // -- 1) wrong PIN must be rejected ------------------------------------
            using (var wsBad = new ClientWebSocket())
            {
                await wsBad.ConnectAsync(new Uri(url), CancellationToken.None);
                await SendAsync(wsBad, new Envelope(
                    Type: Envelope.Types.PairRequest,
                    SessionId: session.SessionId,
                    Pin: "000000",
                    DeviceName: "SelfTestWrongPin"));
                var err = await ReceiveAsync(wsBad);
                Check("wrong PIN rejected",
                    err?.Type == Envelope.Types.Error && err.ErrorCode == Envelope.Errors.BadPin,
                    $"code={err?.ErrorCode}");
            }

            // -- 2) correct pairing ------------------------------------------------
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(url), CancellationToken.None);
            await SendAsync(ws, new Envelope(
                Type: Envelope.Types.PairRequest,
                SessionId: session.SessionId,
                Pin: session.Pin,
                DeviceName: "SelfTest"));
            var ready = await ReceiveAsync(ws);
            var token = ready?.Token;
            Check("pairing accepted",
                ready?.Type == Envelope.Types.SessionReady && ready.Ok == true && !string.IsNullOrEmpty(token),
                $"heartbeat={ready?.HeartbeatIntervalMs}ms");

            // -- 3) ordered edits ----------------------------------------------------
            var r1 = new Envelope(Type: Envelope.Types.TextEdit, Token: token, Revision: 1, DeleteCount: 0,
                InsertText: "مرحبا", Timestamp: Now());
            await SendAsync(ws, r1); lagTotal += Lag(r1.Timestamp); lagCount++;
            var a1 = await ReceiveAsync(ws);
            Check("edit r1 applied", a1?.Applied == true && a1?.Inserts == "مرحبا".Length);

            var r2 = new Envelope(Type: Envelope.Types.TextEdit, Token: token, Revision: 2, DeleteCount: 0,
                InsertText: " Hello", Timestamp: Now());
            await SendAsync(ws, r2); lagTotal += Lag(r2.Timestamp); lagCount++;
            var a2 = await ReceiveAsync(ws);
            Check("edit r2 applied", a2?.Applied == true);

            // -- 4) duplicate revision must be ignored -------------------------------
            // A true duplicate repeats the LAST applied revision (2).
            var dup = new Envelope(Type: Envelope.Types.TextEdit, Token: token, Revision: 2, DeleteCount: 0,
                InsertText: "XXXX", Timestamp: Now());
            await SendAsync(ws, dup);
            var a3 = await ReceiveAsync(ws);
            Check("duplicate ignored", a3?.Applied == false && a3?.Reason == Envelope.Reasons.Duplicate,
                $"applied={a3?.Applied} reason={a3?.Reason}");

            // -- 5) gap must force reset_stream (no wrong text typed) ------------------
            var gap = new Envelope(Type: Envelope.Types.TextEdit, Token: token, Revision: 7, DeleteCount: 0,
                InsertText: "jump", Timestamp: Now());
            await SendAsync(ws, gap);
            Envelope? resetReq = null;
            for (var i = 0; i < 2; i++)
            {
                var m = await ReceiveAsync(ws);
                if (m?.Type == Envelope.Types.ResetStream) { resetReq = m; break; }
            }
            Check("gap triggers reset_stream",
                resetReq is not null
                && resetReq.BaselineRevision == 2
                && resetReq.AppliedLength == "مرحبا Hello".Length,
                $"baseline={resetReq?.BaselineRevision} applied={resetReq?.AppliedLength}");

            // -- 6) resync handshake: adopt baseline, then full rewrite ----------------
            await SendAsync(ws, new Envelope(
                Type: Envelope.Types.ResetStream, Token: token,
                BaselineRevision: resetReq!.BaselineRevision!.Value,
                AppliedLength: resetReq.AppliedLength!.Value));
            var full = "مرحبا Hello World";
            var r3 = new Envelope(Type: Envelope.Types.TextEdit, Token: token, Revision: 3,
                DeleteCount: resetReq.AppliedLength!.Value, InsertText: full, Timestamp: Now());
            await SendAsync(ws, r3); lagTotal += Lag(r3.Timestamp); lagCount++;
            // The server first acks the reset itself (Applied: null), then the rewrite op.
            Envelope? a4 = null;
            for (var i = 0; i < 3; i++)
            {
                var m = await ReceiveAsync(ws);
                if (m?.Type == Envelope.Types.Ack && m.Revision == 3) { a4 = m; break; }
            }
            Check("resync rewrite applied", a4?.Applied == true, $"last={a4?.Type} r{a4?.Revision}");

            // -- 7) heartbeat / ack ------------------------------------------------------
            var hb = new Envelope(Type: Envelope.Types.Heartbeat, Timestamp: Now());
            await SendAsync(ws, hb);
            var hba = await ReceiveAsync(ws);
            Check("heartbeat acked", hba?.Type == Envelope.Types.HeartbeatAck && hba?.Timestamp == hb.Timestamp);

            // -- 8) recorder must hold exactly the applied ops ------------------------
            await Task.Delay(150); // let the pump finish
            List<(int, string)> ops;
            lock (recorder) ops = recorder.Ops.ToList();
            Check("injected ops correct",
                ops.Count == 3
                && ops[0] == (0, "مرحبا")
                && ops[1] == (0, " Hello")
                && ops[2] == ("مرحبا Hello".Length, full),
                string.Join(" | ", ops.Select(o => $"({o.Item1}, {o.Item2})")));

            // -- 9) graceful disconnect ---------------------------------------------------
            await SendAsync(ws, new Envelope(Type: Envelope.Types.Disconnect, Token: token, Reason: "done"));
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
            Check("disconnect clean", true);

            var avgLag = lagCount == 0 ? 0 : lagTotal / lagCount;
            lines.Add($"INFO  measured loopback lag (send→receive): {avgLag:F1} ms avg over {lagCount} messages");
            lines.Add($"INFO  total duration {sw.Elapsed.TotalMilliseconds:F0} ms");
        }
        catch (Exception ex)
        {
            Check("no exceptions", false, ex.Message);
        }
        finally
        {
            _sessions.Invalidate(session);
            if (startedHere) await _server.StopAsync();
        }

        return new SelfTestResult(pass, lines, sw.Elapsed);
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static double Lag(long? timestamp)
        => timestamp is long t ? Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t) : 0;

    private static Task SendAsync(ClientWebSocket ws, Envelope env)
        => ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(ProtocolJson.Serialize(env))),
            WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<Envelope?> ReceiveAsync(ClientWebSocket ws, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
            if (!r.EndOfMessage) continue;
            ProtocolJson.TryParse(Encoding.UTF8.GetString(ms.ToArray()), out var env);
            return env;
        }
    }
}
