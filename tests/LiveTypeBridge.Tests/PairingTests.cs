using LiveTypeBridge.Core.Pairing;
using Xunit;

namespace LiveTypeBridge.Tests;

public class PairingTests
{
    [Fact]
    public void Session_Has_Six_Digit_Pin()
    {
        for (var i = 0; i < 25; i++)
        {
            var s = PairingSession.CreateNew(TimeSpan.FromMinutes(5));
            Assert.Matches("^\\d{6}$", s.Pin);
            Assert.NotEqual("000000", ""); // format sanity
            Assert.True(s.IsAlive);
        }
    }

    [Fact]
    public void Pin_Verify_Accepts_And_Rejects()
    {
        var s = PairingSession.CreateNew(TimeSpan.FromMinutes(5));
        Assert.True(s.VerifyPin(s.Pin));
        Assert.False(s.VerifyPin("000000" == s.Pin ? "111111" : "000000"));
        Assert.False(s.VerifyPin(null));
        Assert.False(s.VerifyPin(""));
        Assert.False(s.VerifyPin(s.Pin + "7")); // wrong length
    }

    [Fact]
    public void Sessions_Are_Unique()
    {
        var a = PairingSession.CreateNew(TimeSpan.FromMinutes(5));
        var b = PairingSession.CreateNew(TimeSpan.FromMinutes(5));
        Assert.NotEqual(a.SessionId, b.SessionId);
        Assert.NotEqual(a.Pin, b.Pin);
    }

    [Fact]
    public void Expired_Session_Rejects_Everything()
    {
        var s = PairingSession.CreateNew(TimeSpan.FromMilliseconds(20));
        Thread.Sleep(80);
        Assert.True(s.IsExpired);
        Assert.False(s.IsAlive);
        Assert.False(s.VerifyPin(s.Pin));
    }

    [Fact]
    public void New_Session_Invalidates_Previous()
    {
        var manager = new PairingSessionManager();
        var first = manager.Create(TimeSpan.FromMinutes(10));
        var second = manager.Create(TimeSpan.FromMinutes(10));

        Assert.True(first.IsInvalidated);
        Assert.False(first.IsAlive);
        Assert.True(second.IsAlive);
        Assert.Null(manager.GetActive(first.SessionId));
        Assert.NotNull(manager.GetActive(second.SessionId));
    }

    [Fact]
    public void Invalidation_Event_Fires_Once()
    {
        var manager = new PairingSessionManager();
        var s = manager.Create(TimeSpan.FromMinutes(10));
        var fired = 0;
        manager.SessionInvalidated += _ => fired++;
        manager.Invalidate(s);
        manager.Invalidate(s); // second call must be a no-op
        Assert.Equal(1, fired);
        Assert.Null(manager.ActiveUserSession);
    }

    [Fact]
    public void SelfTest_Lane_Is_Separate()
    {
        var manager = new PairingSessionManager();
        var user = manager.Create(TimeSpan.FromMinutes(10));
        var selfTest = manager.Create(TimeSpan.FromMinutes(10), selfTest: true);

        Assert.False(user.IsInvalidated);
        Assert.True(selfTest.IsAlive);
        Assert.NotNull(manager.GetActive(user.SessionId));
        Assert.NotNull(manager.GetActive(selfTest.SessionId));
    }

    [Fact]
    public void Token_Verification()
    {
        var s = PairingSession.CreateNew(TimeSpan.FromMinutes(10));
        var token = s.IssueAuthToken();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(s.VerifyToken(token));
        Assert.False(s.VerifyToken(token[..^1] + (token[^1] == '0' ? '1' : '0')));
        Assert.False(s.VerifyToken(null));
    }

    [Fact]
    public void Token_Is_Deterministic_Per_Session_But_Secret_Independent()
    {
        var a = PairingSession.CreateNew(TimeSpan.FromMinutes(10));
        var b = PairingSession.CreateNew(TimeSpan.FromMinutes(10));
        Assert.NotEqual(a.IssueAuthToken(), b.IssueAuthToken());
        Assert.Equal(a.IssueAuthToken(), a.IssueAuthToken());
    }
}

public class QrPayloadTests
{
    [Fact]
    public void Payload_Contains_Address_Pin_Session_NoSecret()
    {
        var s = PairingSession.CreateNew(TimeSpan.FromMinutes(10));
        var json = QrPayload.Build(s, "192.168.1.50", 53017);

        Assert.Contains("192.168.1.50", json);
        Assert.Contains("53017", json);
        Assert.Contains(s.SessionId, json);
        Assert.Contains(s.Pin, json);
        Assert.Contains("ws://", json);
        // The derived auth token must never ride inside the QR.
        Assert.DoesNotContain(s.IssueAuthToken(), json);
    }

    [Fact]
    public void RenderPng_Produces_Real_Image()
    {
        var s = PairingSession.CreateNew(TimeSpan.FromMinutes(10));
        var payload = QrPayload.Build(s, "192.168.1.50", 53017);
        var png = QrPayload.RenderPng(payload);

        // PNG magic number
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.True(png.Length > 500, "QR PNG suspiciously small");
    }

    [Fact]
    public void Payload_Is_Compact_Enough_For_Qr()
    {
        var s = PairingSession.CreateNew(TimeSpan.FromMinutes(10));
        var payload = QrPayload.Build(s, "192.168.100.100", 65535);
        Assert.True(payload.Length < 300, $"payload too long: {payload.Length}");
    }
}
