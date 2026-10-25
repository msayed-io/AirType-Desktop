using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Networking;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Protocol;
using LiveTypeBridge.Core.Settings;
using Xunit;

namespace LiveTypeBridge.Tests;

/// <summary>
/// Regression cover for the duplicated-text complaint: after a reconnect the phone
/// used to declare the mirror length it *believed* in, so when an ack had been lost
/// the PC deleted too few characters and typed the whole line again on top of what
/// was already there.
///
/// The rule these tests pin down: the PC already typed the text, so it is the
/// authority on how many characters the field holds, and the reset ack must report
/// that number so the phone can rewrite from it.
/// </summary>
public sealed class ResyncContractTests
{
    [Fact]
    public async Task Lost_ack_then_resync_rewrites_once_without_duplicating_text()
    {
        var field = new FieldText();
        using var pump = CreatePump(field, out var events);

        // Both ops reached the PC and were typed, but the socket died before their
        // acks made it back, so the phone still believes it only synced "مرحبا".
        pump.HandleEdit(Edit(1, 0, "مرحبا"));
        pump.HandleEdit(Edit(2, 0, " عالم"));
        await events.WaitForRevisionAsync(2);
        Assert.Equal("مرحبا عالم", field.Text);

        pump.HandleReset(Reset(baselineRevision: 0, appliedLength: "مرحبا".Length));
        var resetAck = await events.WaitForReasonAsync("reset");

        // The ack must report what the field really holds (10), not the stale 5.
        Assert.Equal("مرحبا عالم".Length, resetAck.AppliedLength);
        Assert.Equal("reset", resetAck.Reason);

        // The phone rewrites from the reported truth: delete everything, type once.
        pump.HandleEdit(Edit(1, resetAck.AppliedLength!.Value, "مرحبا عالم"));
        await events.WaitForRevisionAsync(1);

        Assert.Equal("مرحبا عالم", field.Text);
    }

    [Fact]
    public async Task Reset_after_a_desktop_restart_adopts_the_phone_claim_and_rewrites_cleanly()
    {
        // Desktop restarted: the pump is empty, but the focused field still holds the
        // text typed by the previous run. Here the phone's claim is the only source.
        var field = new FieldText("مرحبا عالم");
        using var pump = CreatePump(field, out var events);

        pump.HandleReset(Reset(baselineRevision: 0, appliedLength: "مرحبا عالم".Length));
        var resetAck = await events.WaitForReasonAsync("reset");
        Assert.Equal("مرحبا عالم".Length, resetAck.AppliedLength);

        pump.HandleEdit(Edit(1, resetAck.AppliedLength!.Value, "مرحبا عالم"));
        await events.WaitForRevisionAsync(1);

        Assert.Equal("مرحبا عالم", field.Text);
    }

    [Fact]
    public async Task Pause_resume_delta_is_still_applied_as_a_delta()
    {
        var field = new FieldText();
        using var pump = CreatePump(field, out var events);

        pump.HandleEdit(Edit(1, 0, "مرحبا"));
        await events.WaitForRevisionAsync(1);

        pump.PauseManual();
        pump.HandleReset(Reset(baselineRevision: 1, appliedLength: "مرحبا".Length));
        await events.WaitForReasonAsync("reset");
        pump.Resume();

        pump.HandleEdit(Edit(2, 0, " عالم"));
        await events.WaitForRevisionAsync(2);

        Assert.Equal("مرحبا عالم", field.Text);
    }

    [Fact]
    public async Task Socket_level_resync_after_a_lost_ack_leaves_the_line_once()
    {
        var settings = new AppSettings();
        var trusted = new TrustedDeviceStore(settings, () => { });
        var sessions = new PairingSessionManager();
        var field = new FieldText();
        PhoneConnectionManager? manager = null;
        manager = new PhoneConnectionManager(
            sessions,
            trusted,
            session => new PumpCore(field, new PumpEventsBridge(session, manager!)));
        using var connections = manager;
        await using var server = new PhoneLinkServer();
        Assert.True(await server.StartAsync(57417, connections, loopbackOnly: true));
        var uri = new Uri($"ws://127.0.0.1:{server.ActualPort}/livetype");
        var session = sessions.Create(TimeSpan.FromMinutes(5));

        using var phone = new ClientWebSocket();
        await phone.ConnectAsync(uri, CancellationToken.None);
        await SendJsonAsync(phone, new Envelope(
            Type: Envelope.Types.PairRequest,
            SessionId: session.SessionId,
            Pin: session.Pin,
            ClientId: "android_resync",
            DeviceName: "Android · test"));
        var ready = await ReceiveJsonAsync(phone);
        Assert.Equal(Envelope.Types.SessionReady, ready?.Type);
        var token = ready!.Token;

        // Two ops land on the PC; imagine both acks vanish on the way to the phone.
        await SendJsonAsync(phone, new Envelope(
            Type: Envelope.Types.TextEdit, Token: token, Revision: 1, DeleteCount: 0, InsertText: "مرحبا"));
        Assert.True((await ReceiveJsonAsync(phone))?.Applied);
        await SendJsonAsync(phone, new Envelope(
            Type: Envelope.Types.TextEdit, Token: token, Revision: 2, DeleteCount: 0, InsertText: " عالم"));
        Assert.True((await ReceiveJsonAsync(phone))?.Applied);
        Assert.Equal("مرحبا عالم", field.Text);

        // Reconnect resync: the phone only knows it synced 5 characters.
        await SendJsonAsync(phone, new Envelope(
            Type: Envelope.Types.ResetStream, Token: token, BaselineRevision: 0, AppliedLength: "مرحبا".Length));
        var resetAck = await ReceiveJsonAsync(phone);
        Assert.Equal(Envelope.Reasons.Reset, resetAck?.Reason);
        Assert.Equal("مرحبا عالم".Length, resetAck!.AppliedLength);

        // Step two: rewrite from the reported mirror.
        await SendJsonAsync(phone, new Envelope(
            Type: Envelope.Types.TextEdit,
            Token: token,
            Revision: 1,
            DeleteCount: resetAck.AppliedLength!.Value,
            InsertText: "مرحبا عالم"));
        Assert.True((await ReceiveJsonAsync(phone))?.Applied);

        Assert.Equal("مرحبا عالم", field.Text);
    }

    private static Task SendJsonAsync(ClientWebSocket socket, Envelope envelope)
    {
        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<Envelope?> ReceiveJsonAsync(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return ProtocolJson.TryParse(Encoding.UTF8.GetString(buffer, 0, result.Count), out var envelope)
            ? envelope
            : null;
    }

    private static Envelope Edit(int revision, int deleteCount, string insert) => new(
        Type: Envelope.Types.TextEdit,
        Revision: revision,
        DeleteCount: deleteCount,
        InsertText: insert,
        Timestamp: 0);

    private static Envelope Reset(int baselineRevision, int appliedLength) => new(
        Type: Envelope.Types.ResetStream,
        BaselineRevision: baselineRevision,
        AppliedLength: appliedLength);

    /// <summary>Stands in for the focused Windows control: tracks the text it was given.</summary>
    private sealed class FieldText : ITextInjector
    {
        private readonly StringBuilder _text = new();

        public FieldText(string initial = "") => _text.Append(initial);

        public string Text
        {
            get { lock (_text) return _text.ToString(); }
        }

        public bool Inject(int backspaceCount, string text, out string? warningCode)
        {
            warningCode = null;
            lock (_text)
            {
                var deletes = Math.Min(backspaceCount, _text.Length);
                _text.Length -= deletes;
                _text.Append(text);
            }
            return true;
        }
    }

    private sealed class RecordingPumpEvents : IPumpEvents
    {
        private readonly List<Envelope> _sent = [];
        private readonly object _gate = new();

        public Task SendToPhoneAsync(Envelope envelope)
        {
            lock (_gate) _sent.Add(envelope);
            return Task.CompletedTask;
        }

        public Task OnResetNeededAsync(int appliedLength, int expectedNextRevision) => Task.CompletedTask;

        public void OnResetNeeded(int appliedLength, int expectedNextRevision) { }

        public void OnModeChanged(PumpMode mode) { }

        public void OnStats(PumpStats stats) { }

        public void OnInjectorWarning(string code) { }

        public Task<Envelope> WaitForReasonAsync(string reason)
            => WaitForAsync(e => e.Reason == reason && e.Applied == true, $"an ack with reason '{reason}'");

        public Task<Envelope> WaitForRevisionAsync(int revision)
            => WaitForAsync(e => e.Type == Envelope.Types.Ack && e.Revision == revision, $"an ack for revision {revision}");

        private async Task<Envelope> WaitForAsync(Func<Envelope, bool> match, string what)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                lock (_gate)
                {
                    var hit = _sent.LastOrDefault(match);
                    if (hit is not null) return hit;
                }
                await Task.Delay(10);
            }
            throw new TimeoutException($"the pump never sent {what}");
        }
    }

    private static PumpCore CreatePump(ITextInjector injector, out RecordingPumpEvents events)
    {
        events = new RecordingPumpEvents();
        return new PumpCore(injector, events);
    }
}
