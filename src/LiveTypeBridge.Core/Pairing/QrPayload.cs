using System.Text.Json;
using LiveTypeBridge.Core.Pairing;

namespace LiveTypeBridge.Core.Pairing;

/// <summary>Builds the compact JSON payload encoded into the QR (no secrets inside).</summary>
public static class QrPayload
{
    public const int ProtocolVersion = 1;

    public static string Build(PairingSession session, string host, int port)
    {
        var payload = new
        {
            v = ProtocolVersion,
            app = "LiveTypeBridge",
            ws = $"ws://{host}:{port}/livetype",
            host,
            port,
            sid = session.SessionId,
            pin = session.Pin,
            exp = new DateTimeOffset(session.ExpiresUtc).ToUnixTimeMilliseconds(),
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Renders the payload as a high-contrast PNG (black on white).</summary>
    public static byte[] RenderPng(string payload, int pixelsPerModule = 8)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var png = new QRCoder.PngByteQRCode(data);
        return png.GetGraphic(
            pixelsPerModule,
            new byte[] { 17, 24, 39, 255 },    // dark: near-black
            new byte[] { 255, 255, 255, 255 }); // light: white
    }
}
