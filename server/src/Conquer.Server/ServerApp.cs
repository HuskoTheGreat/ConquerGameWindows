using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Conquer.Server.Bots;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace Conquer.Server
{
    /// <summary>
    /// The whole server pipeline: services, limits and the /ws endpoint. The public server (Program) and a game
    /// hosted from inside the client over the local network (<see cref="LanServer"/>) both run exactly this.
    /// </summary>
    public static class ServerApp
    {
        public static WebApplication Build(WebApplicationBuilder builder, ServerOptions options)
        {
            builder.WebHost.UseUrls(options.ListenUrl);
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.AddServerHeader = false;
                k.Limits.MaxConcurrentConnections = options.MaxConnections + 20;
                k.Limits.MaxConcurrentUpgradedConnections = options.MaxConnections;
                k.Limits.MaxRequestBodySize = 4 * 1024;
                k.Limits.MaxRequestHeadersTotalSize = 8 * 1024;
                k.Limits.MaxRequestLineSize = 2 * 1024;
                k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            });

            var stopwatch = Stopwatch.StartNew();
            Func<double> clock = () => stopwatch.Elapsed.TotalSeconds;

            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(clock);
            builder.Services.AddSingleton<ILlmBackend>(_ => new OpenAiCompatibleBackend(
                new HttpClient(new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    MaxConnectionsPerServer = 2,
                    UseProxy = false,
                })
                { Timeout = TimeSpan.FromSeconds(options.Bots.TimeoutSeconds + 5) },
                options.Bots));
            builder.Services.AddSingleton<BotDirector>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<BotDirector>());
            builder.Services.AddSingleton<RoomRegistry>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<RoomRegistry>());
            builder.Services.AddSingleton<GameServer>();

            var app = builder.Build();

            if (options.BehindProxy)
            {
                // Only the local reverse proxy may say who the client is; anyone else's X-Forwarded-For is ignored,
                // so per-IP limits can't be dodged by faking the header.
                var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor, ForwardLimit = 1 };
                forwarded.KnownNetworks.Clear();
                forwarded.KnownProxies.Clear();
                forwarded.KnownProxies.Add(IPAddress.Loopback);
                forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
                app.UseForwardedHeaders(forwarded);
            }

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

            app.MapGet("/healthz", () => Results.Text("ok"));

            var server = app.Services.GetRequiredService<GameServer>();
            app.Map("/ws", async (HttpContext context) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!options.AllowBrowserOrigins && context.Request.Headers.ContainsKey("Origin"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                string ip = ClientAddress.Key(context.Connection.RemoteIpAddress);
                if (!server.TryAdmit(ip))
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    return;
                }

                System.Net.WebSockets.WebSocket socket;
                try
                {
                    socket = await context.WebSockets.AcceptWebSocketAsync();
                }
                catch
                {
                    server.Release(ip);
                    throw;
                }
                await server.RunAsync(socket, ip); // releases the slot when the client goes
            });

            return app;
        }
    }
}
