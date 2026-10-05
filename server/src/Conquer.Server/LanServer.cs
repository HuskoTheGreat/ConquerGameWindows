using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conquer.Server
{
    /// <summary>
    /// The game server, run inside the game itself so one player can host for others on the same network.
    /// Same pipeline and limits as the public server, tuned for one room: no reverse proxy, no chat bots, one room
    /// (the host's), bigger boards allowed, and longer idle times since nobody else shares the machine.
    /// </summary>
    public sealed class LanServer : IAsyncDisposable
    {
        /// <summary>The first port tried; the next few are tried if it's taken.</summary>
        public const int DefaultPort = 47620;
        const int PortsToTry = 10;

        readonly WebApplication _app;

        public int Port { get; }

        LanServer(WebApplication app, int port)
        {
            _app = app;
            Port = port;
        }

        /// <summary>Options for a LAN game listening on <paramref name="port"/> on every network interface.</summary>
        public static ServerOptions LanOptions(int port) => new ServerOptions
        {
            ListenUrl = $"http://0.0.0.0:{port}",
            BehindProxy = false,
            MaxConnections = 24,
            MaxConnectionsPerIp = 8,
            MaxRooms = 1,
            MaxRoomsPerIp = 1,
            MaxBoardRadius = 6,
            EmptyLobbyMinutes = 60,
            EmptyGameMinutes = 60,
            FinishedGameMinutes = 60,
            MaxRoomHours = 24,
            Bots = new BotOptions { Enabled = false },
        };

        /// <summary>Starts listening, or throws <see cref="InvalidOperationException"/> with a message a person can read.</summary>
        public static async Task<LanServer> StartAsync(int firstPort = DefaultPort)
        {
            for (int port = firstPort; port < firstPort + PortsToTry; port++)
            {
                var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
                {
                    Args = Array.Empty<string>(),
                    ContentRootPath = AppContext.BaseDirectory,
                });
                builder.Logging.ClearProviders();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                WebApplication app = ServerApp.Build(builder, LanOptions(port));
                try
                {
                    await app.StartAsync();
                    return new LanServer(app, port);
                }
                catch (IOException)
                {
                    // Port in use (Kestrel reports it as an IOException); try the next one.
                    await app.DisposeAsync();
                }
            }
            throw new InvalidOperationException($"Couldn't open a port for the game (tried {firstPort}-{firstPort + PortsToTry - 1}).");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _app.StopAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException)
            {
            }
            await _app.DisposeAsync();
        }
    }
}
