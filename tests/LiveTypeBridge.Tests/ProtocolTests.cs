using LiveTypeBridge.Core.Protocol;
using Xunit;

namespace LiveTypeBridge.Tests;

public class ProtocolTests
{
    [Fact]
    public void Envelope_RoundTrips_AllFields()
    {
        var env = new Envelope(
            Type: Envelope.Types.TextEdit,
            SessionId: "abc123",
            Pin: "123456",
            DeviceName: "Pixel",
            ClientId: "android_123",
            Token: "tok",
            Ok: true,
            ErrorCode: null,
            ErrorMessage: null,
            Revision: 42,
            DeleteCount: 3,
            InsertText: "مرحبا 🌍",
            Timestamp: 1234567890);

        var json = ProtocolJson.Serialize(env);
        Assert.True(ProtocolJson.TryParse(json, out var parsed));

        Assert.Equal(Envelope.Types.TextEdit, parsed.Type);
        Assert.Equal("abc123", parsed.SessionId);
        Assert.Equal("123456", parsed.Pin);
        Assert.Equal("Pixel", parsed.DeviceName);
        Assert.Equal("android_123", parsed.ClientId);
        Assert.Equal("tok", parsed.Token);
        Assert.True(parsed.Ok);
        Assert.Equal(42, parsed.Revision);
        Assert.Equal(3, parsed.DeleteCount);
        Assert.Equal("مرحبا 🌍", parsed.InsertText);
        Assert.Equal(1234567890, parsed.Timestamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"Type\":}")]
    public void TryParse_Rejects_Malformed(string json)
    {
        Assert.False(ProtocolJson.TryParse(json, out _));
    }

    [Fact]
    public void TryParse_Rejects_Oversized()
    {
        var big = "{\"Type\":\"text_edit\",\"InsertText\":\"" + new string('a', 200 * 1024) + "\"}";
        Assert.False(ProtocolJson.TryParse(big, out _));
    }

    [Fact]
    public void TryParse_Rejects_MissingType()
    {
        Assert.False(ProtocolJson.TryParse("{\"Revision\":1}", out _));
    }

    [Fact]
    public void Unknown_Fields_Are_Ignored_ForwardCompatibility()
    {
        const string json = "{\"Type\":\"text_edit\",\"Revision\":5,\"FutureField\":\"x\"}";
        Assert.True(ProtocolJson.TryParse(json, out var env));
        Assert.Equal(5, env.Revision);
    }
}

public class StreamStateTests
{
    [Fact]
    public void First_Revision_Must_Be_One()
    {
        var s = new StreamState();
        var gap = s.Apply(2, 0, "hi");
        Assert.Equal(EditDecision.Gap, gap.Decision);
        Assert.Equal(1, gap.ExpectedNextRevision);
    }

    [Fact]
    public void Ordered_Revisions_Applied()
    {
        var s = new StreamState();
        Assert.Equal(EditDecision.Applied, s.Apply(1, 0, "a").Decision);
        Assert.Equal(EditDecision.Applied, s.Apply(2, 0, "b").Decision);
        Assert.Equal(2, s.LastRevision);
    }

    [Fact]
    public void Duplicate_Ignored()
    {
        var s = new StreamState();
        s.Apply(1, 0, "a");
        var dup = s.Apply(1, 0, "a");
        Assert.Equal(EditDecision.Duplicate, dup.Decision);
        Assert.Equal(0, dup.Backspaces);
        Assert.Equal(string.Empty, dup.InsertText);
    }

    [Fact]
    public void Old_Revision_Ignored()
    {
        var s = new StreamState();
        s.Apply(1, 0, "a");
        s.Apply(2, 0, "b");
        var old = s.Apply(1, 0, "c");
        Assert.Equal(EditDecision.Old, old.Decision);
    }

    [Fact]
    public void Gap_Reports_Expected_Next()
    {
        var s = new StreamState();
        s.Apply(1, 0, "a");
        s.Apply(2, 0, "b");
        var gap = s.Apply(5, 0, "zzz");
        Assert.Equal(EditDecision.Gap, gap.Decision);
        Assert.Equal(3, gap.ExpectedNextRevision);
        Assert.Equal(2, s.LastRevision); // state untouched
    }

    [Fact]
    public void DeleteCount_Converts_To_Backspace_Clamped()
    {
        var s = new StreamState();
        s.Apply(1, 0, "abc");
        var edit = s.Apply(2, 10, "d");
        Assert.Equal(EditDecision.Applied, edit.Decision);
        Assert.Equal(3, edit.Backspaces); // cannot delete more than we typed
        Assert.Equal(1, s.AppliedLength);
    }

    [Fact]
    public void Surrogate_Pairs_Count_As_Two_Code_Units()
    {
        var s = new StreamState();
        s.Apply(1, 0, "🌍"); // one emoji = 2 UTF-16 units
        Assert.Equal(2, s.AppliedLength);
    }

    [Fact]
    public void Invalid_Inputs_Rejected()
    {
        var s = new StreamState();
        Assert.Equal(EditDecision.Invalid, s.Apply(0, 0, "x").Decision);
        Assert.Equal(EditDecision.Invalid, s.Apply(-1, 0, "x").Decision);
        Assert.Equal(EditDecision.Invalid, s.Apply(1, -2, "x").Decision);
    }

    [Fact]
    public void ResetTo_Adopts_Phone_Baseline()
    {
        var s = new StreamState();
        s.Apply(1, 0, "abc");
        s.ResetTo(5, 7);
        Assert.Equal(5, s.LastRevision);
        Assert.Equal(7, s.AppliedLength);
        Assert.Equal(EditDecision.Applied, s.Apply(6, 0, "x").Decision);
    }

    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\0b", "ab")]
    [InlineData("tab\there", "tab\there")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Sanitize_Normalizes_Text(string? input, string expected)
    {
        Assert.Equal(expected, StreamState.Sanitize(input));
    }
}
