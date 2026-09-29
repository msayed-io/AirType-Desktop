using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LiveTypeBridge.Core.Networking;

/// <summary>
/// Owns the embedded Kestrel host. Binds to all LAN interfaces (not just localhost)
/// and walks up the port range automatically when the preferred port is occupied.
/// </summary>
public sealed class PhoneLinkServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private WebApplication? _app;
    private DateTime _startedUtc;

    public int ActualPort { get; private set; }
    public bool IsRunning { get; private set; }
    public TimeSpan Uptime => IsRunning ? DateTime.UtcNow - _startedUtc : TimeSpan.Zero;

    public event Action<string>? ServerLog;

    /// <param name="loopbackOnly">Diagnostics/self-test mode: bind 127.0.0.1 only so no firewall prompt appears.</param>
    public async Task<bool> StartAsync(int preferredPort, PhoneConnectionManager connections, bool loopbackOnly = false)
    {
        lock (_gate)
        {
            if (IsRunning) return true;
        }

        for (var port = preferredPort; port < preferredPort + 25; port++)
        {
            var app = BuildApp(connections);
            try
            {
                app.Urls.Add(loopbackOnly ? $"http://127.0.0.1:{port}" : $"http://*:{port}");
                await app.StartAsync();
            }
            catch (Exception ex) when (IsAddressInUse(ex))
            {
                await DisposeQuietly(app);
                ServerLog?.Invoke($"Port {port} busy, trying next…");
                continue;
            }

            lock (_gate)
            {
                _app = app;
                ActualPort = port;
                IsRunning = true;
                _startedUtc = DateTime.UtcNow;
            }
            ServerLog?.Invoke($"Listening on {(loopbackOnly ? "127.0.0.1" : "0.0.0.0")}:{port}");
            return true;
        }
        return false;
    }

    public async Task StopAsync()
    {
        WebApplication? app;
        lock (_gate)
        {
            app = _app;
            _app = null;
            IsRunning = false;
        }
        if (app is not null)
        {
            await DisposeQuietly(app);
            ServerLog?.Invoke("Server stopped.");
        }
    }

    private static WebApplication BuildApp(PhoneConnectionManager connections)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
            ApplicationName = "LiveTypeBridge",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15),
        });
        app.Map("/livetype", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await connections.HandleSocketAsync(ctx);
        });
        return app;
    }

    private static bool IsAddressInUse(Exception ex)
    {
        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
        {
            if (e is System.IO.IOException) return true;
            if (e.GetType().Name.Contains("AddressInUse", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task DisposeQuietly(WebApplication app)
    {
        try { await app.DisposeAsync(); } catch { /* shutting down */ }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
