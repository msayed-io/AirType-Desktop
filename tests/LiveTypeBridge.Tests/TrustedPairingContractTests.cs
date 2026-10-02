using System.Net.WebSockets;
using System.Text;
using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Networking;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Protocol;
using LiveTypeBridge.Core.Settings;
using Xunit;

namespace LiveTypeBridge.Tests;

public sealed class TrustedPairingContractTests
{
    [Fact]
    public async Task Pin_pairing_persists_token_then_same_token_reconnects_without_pin()
    {
        var settings = new AppSettings();
        var trusted = new TrustedDeviceStore(settings, () => { });
        var sessions = new PairingSessionManager();
        using var connections = new PhoneConnectionManager(
            sessions, trusted, _ => new PumpCore(new NoOpInjector(), new NoOpPumpEvents()));
        await using var server = new PhoneLinkServer();
        Assert.True(await server.StartAsync(57017, connections, loopbackOnly: true));
        var uri = new Uri($"ws://127.0.0.1:{server.ActualPort}/livetype");

        var firstSession = sessions.Create(TimeSpan.FromMinutes(5));
        using var firstPhone = new ClientWebSocket();
        await firstPhone.ConnectAsync(uri, CancellationToken.None);
        await SendAsync(firstPhone, new Envelope(
            Type: Envelope.Types.PairRequest,
            SessionId: firstSession.SessionId,
            Pin: firstSession.Pin,
            DeviceName: "Android · Samsung S24",
            ClientId: "android_9a8b7c6d5e4f3a2b"));
        var firstReady = await ReceiveAsync(firstPhone);

        Assert.Equal(Envelope.Types.SessionReady, firstReady?.Type);
        Assert.True(firstReady?.Ok);
        Assert.Equal(PhoneConnectionManager.HeartbeatIntervalMs, firstReady?.HeartbeatIntervalMs);
        Assert.Equal(2500, firstReady?.HeartbeatIntervalMs);
        Assert.NotNull(firstReady?.Token);
        Assert.Equal(64, firstReady!.Token!.Length);
        Assert.Single(settings.TrustedDevices);
        Assert.DoesNotContain(settings.TrustedDevices, device => device.TokenHash == firstReady.Token);

        await SendAsync(firstPhone, new Envelope(
            Type: Envelope.Types.Disconnect,
            Token: firstReady.Token,
            Reason: "test"));
        await Task.Delay(100);

        var reconnectSession = sessions.Create(TimeSpan.FromMinutes(5));
        using var returningPhone = new ClientWebSocket();
        await returningPhone.ConnectAsync(uri, CancellationToken.None);
        await SendAsync(returningPhone, new Envelope(
            Type: Envelope.Types.PairRequest,
            SessionId: reconnectSession.SessionId,
            ClientId: "android_9a8b7c6d5e4f3a2b",
            Token: firstReady.Token,
            DeviceName: "Android · Samsung S24"));
        var reconnectReady = await ReceiveAsync(returningPhone);

        Assert.Equal(Envelope.Types.SessionReady, reconnectReady?.Type);
        Assert.True(reconnectReady?.Ok);
        Assert.Equal(firstReady.Token, reconnectReady?.Token);
        Assert.Equal(2500, reconnectReady?.HeartbeatIntervalMs);
    }

    [Fact]
    public async Task Unexpected_socket_reconnect_keeps_revision_mirror_and_ignores_replay()
    {
        var settings = new AppSettings();
        var trusted = new TrustedDeviceStore(settings, () => { });
        var sessions = new PairingSessionManager();
        var injector = new RecordingInjector();
        PhoneConnectionManager? manager = null;
        manager = new PhoneConnectionManager(
            sessions,
            trusted,
            session => new PumpCore(injector, new PumpEventsBridge(session, manager!)));
        using var connections = manager;
        await using var server = new PhoneLinkServer();
        Assert.True(await server.StartAsync(57317, connections, loopbackOnly: true));
        var uri = new Uri($"ws://127.0.0.1:{server.ActualPort}/livetype");
        var session = sessions.Create(TimeSpan.FromMinutes(5));

        string token;
        using (var firstPhone = new ClientWebSocket())
        {
            await firstPhone.ConnectAsync(uri, CancellationToken.None);
            await SendAsync(firstPhone, new Envelope(
                Type: Envelope.Types.PairRequest,
                SessionId: session.SessionId,
                Pin: session.Pin,
                ClientId: "android_reconnect",
                DeviceName: "Phone"));
            token = (await ReceiveAsync(firstPhone))!.Token!;
            await SendAsync(firstPhone, new Envelope(
                Type: Envelope.Types.TextEdit,
                Token: token,
                Revision: 1,
                DeleteCount: 0,
                InsertText: "once"));
            Assert.True((await ReceiveAsync(firstPhone))?.Applied);
            await firstPhone.CloseAsync(WebSocketCloseStatus.NormalClosure, "network drop", CancellationToken.None);
        }
        await Task.Delay(100);

        using var returningPhone = new ClientWebSocket();
        await returningPhone.ConnectAsync(uri, CancellationToken.None);
        await SendAsync(returningPhone, new Envelope(
            Type: Envelope.Types.PairRequest,
            SessionId: session.SessionId,
            ClientId: "android_reconnect",
            Token: token,
            DeviceName: "Phone"));
        Assert.Equal(Envelope.Types.SessionReady, (await ReceiveAsync(returningPhone))?.Type);

        // Replaying revision 1 after the socket replacement must not type it twice.
        await SendAsync(returningPhone, new Envelope(
            Type: Envelope.Types.TextEdit,
            Token: token,
            Revision: 1,
            DeleteCount: 0,
            InsertText: "once"));
        var replayAck = await ReceiveAsync(returningPhone);

        Assert.False(replayAck?.Applied);
        Assert.Equal(Envelope.Reasons.Duplicate, replayAck?.Reason);
        Assert.Equal(new[] { (0, "once") }, injector.Ops);
    }

    [Fact]
    public async Task Legacy_pin_client_without_client_id_keeps_ephemeral_pairing_flow()
    {
        var settings = new AppSettings();
        var trusted = new TrustedDeviceStore(settings, () => { });
        var sessions = new PairingSessionManager();
        using var connections = new PhoneConnectionManager(
            sessions, trusted, _ => new PumpCore(new NoOpInjector(), new NoOpPumpEvents()));
        await using var server = new PhoneLinkServer();
        Assert.True(await server.StartAsync(57217, connections, loopbackOnly: true));
        var session = sessions.Create(TimeSpan.FromMinutes(5));

        using var legacyPhone = new ClientWebSocket();
        await legacyPhone.ConnectAsync(
            new Uri($"ws://127.0.0.1:{server.ActualPort}/livetype"),
            CancellationToken.None);
        await SendAsync(legacyPhone, new Envelope(
            Type: Envelope.Types.PairRequest,
            SessionId: session.SessionId,
            Pin: session.Pin,
            DeviceName: "Legacy Android"));
        var ready = await ReceiveAsync(legacyPhone);

        Assert.Equal(Envelope.Types.SessionReady, ready?.Type);
        Assert.True(ready?.Ok);
        Assert.False(string.IsNullOrWhiteSpace(ready?.Token));
        Assert.Empty(settings.TrustedDevices);
    }

    [Fact]
    public async Task Invalid_or_revoked_persistent_token_returns_exact_bad_token_error()
    {
        var settings = new AppSettings();
        var trusted = new TrustedDeviceStore(settings, () => { });
        var credential = trusted.Enroll("android_phone", "Phone");
        trusted.Revoke(credential.ClientId);
        var sessions = new PairingSessionManager();
        using var connections = new PhoneConnectionManager(
            sessions, trusted, _ => new PumpCore(new NoOpInjector(), new NoOpPumpEvents()));
        await using var server = new PhoneLinkServer();
        Assert.True(await server.StartAsync(57117, connections, loopbackOnly: true));
        var session = sessions.Create(TimeSpan.FromMinutes(5));

        using var phone = new ClientWebSocket();
        await phone.ConnectAsync(new Uri($"ws://127.0.0.1:{server.ActualPort}/livetype"), CancellationToken.None);
        await SendAsync(phone, new Envelope(
            Type: Envelope.Types.PairRequest,
            SessionId: session.SessionId,
            ClientId: credential.ClientId,
            Token: credential.Token,
            DeviceName: "Phone"));
        var error = await ReceiveAsync(phone);

        Assert.Equal(Envelope.Types.Error, error?.Type);
        Assert.Equal(Envelope.Errors.BadToken, error?.ErrorCode);
        Assert.Equal("Device token is invalid or revoked", error?.ErrorMessage);
    }

    private static Task SendAsync(ClientWebSocket socket, Envelope envelope)
    {
        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<Envelope?> ReceiveAsync(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return ProtocolJson.TryParse(Encoding.UTF8.GetString(buffer, 0, result.Count), out var envelope)
            ? envelope
            : null;
    }

    private sealed class RecordingInjector : ITextInjector
    {
        public List<(int Backspaces, string Text)> Ops { get; } = [];

        public bool Inject(int backspaceCount, string text, out string? warningCode)
        {
            warningCode = null;
            Ops.Add((backspaceCount, text));
            return true;
        }
    }

    private sealed class NoOpInjector : ITextInjector
    {
        public bool Inject(int backspaceCount, string text, out string? warningCode)
        {
            warningCode = null;
            return true;
        }
    }

    private sealed class NoOpPumpEvents : IPumpEvents
    {
        public Task SendToPhoneAsync(Envelope envelope) => Task.CompletedTask;
        public Task OnResetNeededAsync(int appliedLength, int expectedNextRevision) => Task.CompletedTask;
        public void OnResetNeeded(int appliedLength, int expectedNextRevision) { }
        public void OnModeChanged(PumpMode mode) { }
        public void OnStats(PumpStats stats) { }
        public void OnInjectorWarning(string code) { }
    }
}
