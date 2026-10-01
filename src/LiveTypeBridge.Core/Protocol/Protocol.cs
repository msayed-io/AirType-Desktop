using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveTypeBridge.Core.Protocol;

/// <summary>
/// Single JSON envelope used by every message in both directions. Keeping one flat
/// envelope makes the wire format tolerant to new optional fields (forward compatibility)
/// and trivially testable. All properties are camelCase on the wire.
/// </summary>
public sealed record Envelope(
    string? Type = null,
    string? SessionId = null,
    string? Pin = null,
    string? DeviceName = null,
    string? ClientId = null,
    string? Token = null,
    bool? Ok = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    int? Revision = null,
    int? DeleteCount = null,
    string? InsertText = null,
    long? Timestamp = null,
    int? LastRttMs = null,
    int? HeartbeatIntervalMs = null,
    long? ServerTimeUtc = null,
    int? BaselineRevision = null,
    int? AppliedLength = null,
    string? Reason = null,
    bool? Applied = null,
    int? Backspaces = null,
    int? Inserts = null,
    long? ReceivedAtUtc = null)
{
    public static class Types
    {
        public const string PairRequest = "pair_request";
        public const string SessionReady = "session_ready";
        public const string TextEdit = "text_edit";
        public const string Ack = "ack";
        public const string ResetStream = "reset_stream";
        public const string Heartbeat = "heartbeat";
        public const string HeartbeatAck = "heartbeat_ack";
        public const string Disconnect = "disconnect";
        public const string Error = "error";
    }

    public static class Errors
    {
        public const string BadPin = "bad_pin";
        public const string UnknownSession = "unknown_session";
        public const string SessionExpired = "session_expired";
        public const string AlreadyPaired = "already_paired";
        public const string BadToken = "bad_token";
        public const string UnexpectedMessage = "unexpected_message";
        public const string Malformed = "malformed_message";
        public const string TooLarge = "message_too_large";
        public const string ServerClosing = "server_closing";
    }

    public static class Reasons
    {
        public const string Duplicate = "duplicate";
        public const string Old = "old";
        public const string Resync = "resync";
        public const string Paused = "paused";
        public const string LostSync = "lost_sync";
        public const string PhoneClosed = "phone_closed";
        public const string Replaced = "replaced";
        public const string UserDisconnect = "user_disconnect";
        public const string Stale = "stale_connection";
    }
}

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, // tolerate "Type" vs "type" on the wire
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(Envelope e) => JsonSerializer.Serialize(e, Options);

    public static bool TryParse(string json, [NotNullWhen(true)] out Envelope? msg)
    {
        msg = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 128 * 1024) return false;
        try
        {
            msg = JsonSerializer.Deserialize<Envelope>(json, Options);
            return !string.IsNullOrEmpty(msg?.Type);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
