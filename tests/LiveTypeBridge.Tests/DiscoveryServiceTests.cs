using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LiveTypeBridge.Core.Networking;
using Xunit;

namespace LiveTypeBridge.Tests;

public sealed class DiscoveryServiceTests
{
    [Fact]
    public async Task Discover_request_receives_exact_beacon_contract()
    {
        var discoveryPort = ReserveUdpPort();
        await using var service = new LiveTypeDiscoveryService(
            discoveryPort, TimeSpan.FromHours(1));
        var ad = new LiveTypeAdvertisement(
            "Mohamed-PC", "192.168.1.15", 53017, "session-abc", DateTime.UtcNow.AddMinutes(5));

        Assert.True(await service.StartAsync(ad, broadcast: false));

        using var phone = new UdpClient(AddressFamily.InterNetwork);
        var request = Encoding.UTF8.GetBytes("{\"type\":\"livetype_discover\"}");
        await phone.SendAsync(request, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var response = await phone.ReceiveAsync(timeout.Token);

        using var json = JsonDocument.Parse(response.Buffer);
        var root = json.RootElement;
        Assert.Equal("livetype_beacon", root.GetProperty("type").GetString());
        Assert.Equal("Mohamed-PC", root.GetProperty("name").GetString());
        Assert.Equal("192.168.1.15", root.GetProperty("host").GetString());
        Assert.Equal(53017, root.GetProperty("port").GetInt32());
        Assert.Equal("session-abc", root.GetProperty("sessionId").GetString());
        Assert.Equal(5, root.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("{\"type\":\"other\"}")]
    [InlineData("{\"message\":\"livetype_discover\"}")]
    [InlineData("not-json")]
    public async Task Invalid_or_lookalike_payload_gets_no_response(string payload)
    {
        var discoveryPort = ReserveUdpPort();
        await using var service = new LiveTypeDiscoveryService(
            discoveryPort, TimeSpan.FromHours(1));
        var ad = new LiveTypeAdvertisement(
            "PC", "192.168.1.20", 53017, "sid", DateTime.UtcNow.AddMinutes(5));
        Assert.True(await service.StartAsync(ad, broadcast: false));

        using var phone = new UdpClient(AddressFamily.InterNetwork);
        await phone.SendAsync(
            Encoding.UTF8.GetBytes(payload),
            new IPEndPoint(IPAddress.Loopback, discoveryPort));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await phone.ReceiveAsync(timeout.Token));
    }

    [Fact]
    public async Task Stop_releases_discovery_port_for_immediate_restart()
    {
        var discoveryPort = ReserveUdpPort();
        var ad = new LiveTypeAdvertisement(
            "PC", "10.0.0.2", 53017, "sid", DateTime.UtcNow.AddMinutes(5));
        await using var first = new LiveTypeDiscoveryService(discoveryPort);
        Assert.True(await first.StartAsync(ad, broadcast: false));
        await first.StopAsync();

        await using var second = new LiveTypeDiscoveryService(discoveryPort);
        Assert.True(await second.StartAsync(ad, broadcast: false));
    }

    private static int ReserveUdpPort()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
