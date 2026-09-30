using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveTypeBridge.Core.Networking;

public sealed record LiveTypeAdvertisement(
    string Name,
    string Host,
    int Port,
    string SessionId,
    DateTime ExpiresUtc);

/// <summary>
/// LAN discovery responder for the Android client. While a pairing window is active it
/// listens for {"type":"livetype_discover"} on UDP 53018, replies directly to the
/// sender, and periodically advertises the same beacon by IPv4 broadcast.
/// </summary>
public sealed class LiveTypeDiscoveryService : IAsyncDisposable, IDisposable
{
    public const int DefaultDiscoveryPort = 53018;
    public static readonly TimeSpan DefaultBroadcastInterval = TimeSpan.FromMilliseconds(2500);

    private const int MaxDatagramBytes = 1024;
    private static readonly TimeSpan MinimumReplyInterval = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly int _discoveryPort;
    private readonly TimeSpan _broadcastInterval;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<IPAddress, DateTime> _lastReplyUtc = new();
    private CancellationTokenSource? _cts;
    private UdpClient? _udp;
    private Task? _receiveTask;
    private Task? _broadcastTask;

    public LiveTypeDiscoveryService(
        int discoveryPort = DefaultDiscoveryPort,
        TimeSpan? broadcastInterval = null)
    {
        if (discoveryPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(discoveryPort));

        _discoveryPort = discoveryPort;
        _broadcastInterval = broadcastInterval ?? DefaultBroadcastInterval;
        if (_broadcastInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(broadcastInterval));
    }

    public bool IsRunning
    {
        get { lock (_gate) return _udp is not null; }
    }

    public event Action<string>? DiscoveryLog;

    public async Task<bool> StartAsync(LiveTypeAdvertisement advertisement, bool broadcast = true)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        ValidateAdvertisement(advertisement);
        await StopAsync().ConfigureAwait(false);

        UdpClient udp;
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
            udp.EnableBroadcast = true;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            DiscoveryLog?.Invoke($"Unable to bind UDP 0.0.0.0:{_discoveryPort}: {ex.Message}");
            return false;
        }

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _udp = udp;
            _cts = cts;
            _lastReplyUtc.Clear();
            _receiveTask = ReceiveLoopAsync(udp, advertisement, cts.Token);
            _broadcastTask = broadcast
                ? BroadcastLoopAsync(udp, advertisement, cts.Token)
                : Task.CompletedTask;
        }

        DiscoveryLog?.Invoke($"Listening on UDP 0.0.0.0:{_discoveryPort} for LAN discovery");
        return true;
    }

    public async Task StopAsync()
    {
        UdpClient? udp;
        CancellationTokenSource? cts;
        Task? receiveTask;
        Task? broadcastTask;
        lock (_gate)
        {
            udp = _udp;
            cts = _cts;
            receiveTask = _receiveTask;
            broadcastTask = _broadcastTask;
            _udp = null;
            _cts = null;
            _receiveTask = null;
            _broadcastTask = null;
            _lastReplyUtc.Clear();
        }

        if (udp is null) return;
        try { cts?.Cancel(); } catch { }
        try { udp.Dispose(); } catch { }
        await IgnoreCancellationAsync(receiveTask).ConfigureAwait(false);
        await IgnoreCancellationAsync(broadcastTask).ConfigureAwait(false);
        cts?.Dispose();
        DiscoveryLog?.Invoke("LAN discovery stopped");
    }

    private async Task ReceiveLoopAsync(
        UdpClient udp,
        LiveTypeAdvertisement advertisement,
        CancellationToken token)
    {
        var response = SerializeBeacon(advertisement);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var received = await udp.ReceiveAsync(token).ConfigureAwait(false);
                if (DateTime.UtcNow >= advertisement.ExpiresUtc) return;
                if (received.Buffer.Length is 0 or > MaxDatagramBytes) continue;
                if (!IsDiscoveryRequest(received.Buffer)) continue;
                if (!IsLanAddress(received.RemoteEndPoint.Address)) continue;
                if (!CanReply(received.RemoteEndPoint.Address)) continue;

                await SendAsync(udp, response, received.RemoteEndPoint, token).ConfigureAwait(false);
                DiscoveryLog?.Invoke($"Discovery reply sent to {received.RemoteEndPoint.Address}");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException ex) when (token.IsCancellationRequested) { _ = ex; }
        catch (Exception ex)
        {
            DiscoveryLog?.Invoke($"Discovery receive error: {ex.Message}");
        }
    }

    private async Task BroadcastLoopAsync(
        UdpClient udp,
        LiveTypeAdvertisement advertisement,
        CancellationToken token)
    {
        var response = SerializeBeacon(advertisement);
        var target = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
        try
        {
            while (!token.IsCancellationRequested && DateTime.UtcNow < advertisement.ExpiresUtc)
            {
                try
                {
                    await SendAsync(udp, response, target, token).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    DiscoveryLog?.Invoke($"Discovery broadcast error: {ex.Message}");
                }
                await Task.Delay(_broadcastInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
    }

    private async Task SendAsync(
        UdpClient udp,
        byte[] payload,
        IPEndPoint target,
        CancellationToken token)
    {
        await _sendGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await udp.SendAsync(payload, target, token).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private bool CanReply(IPAddress address)
    {
        var now = DateTime.UtcNow;
        while (true)
        {
            if (!_lastReplyUtc.TryGetValue(address, out var previous))
                return _lastReplyUtc.TryAdd(address, now);
            if (now - previous < MinimumReplyInterval) return false;
            if (_lastReplyUtc.TryUpdate(address, now, previous)) return true;
        }
    }

    internal static bool IsDiscoveryRequest(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("type", out var type)
                   && type.ValueKind == JsonValueKind.String
                   && string.Equals(type.GetString(), "livetype_discover", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static byte[] SerializeBeacon(LiveTypeAdvertisement advertisement)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new BeaconPayload(
            Type: "livetype_beacon",
            Name: advertisement.Name,
            Host: advertisement.Host,
            Port: advertisement.Port,
            SessionId: advertisement.SessionId), JsonOptions));

    internal static bool IsLanAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
               || bytes[0] == 127
               || bytes[0] == 192 && bytes[1] == 168
               || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
               || bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }

    private static void ValidateAdvertisement(LiveTypeAdvertisement advertisement)
    {
        if (string.IsNullOrWhiteSpace(advertisement.Name))
            throw new ArgumentException("Device name is required", nameof(advertisement));
        if (!IPAddress.TryParse(advertisement.Host, out var host)
            || host.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(host)
            || host.Equals(IPAddress.Any))
            throw new ArgumentException("A LAN IPv4 host is required", nameof(advertisement));
        if (advertisement.Port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(advertisement));
        if (string.IsNullOrWhiteSpace(advertisement.SessionId))
            throw new ArgumentException("Session ID is required", nameof(advertisement));
        if (advertisement.ExpiresUtc <= DateTime.UtcNow)
            throw new ArgumentException("Session is already expired", nameof(advertisement));
    }

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed record BeaconPayload(
        string Type,
        string Name,
        string Host,
        int Port,
        string SessionId);
}
