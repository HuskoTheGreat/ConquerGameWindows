using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Catan.Server;
using Catan.Server.Bots;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateSlimBuilder(args);

IConfigurationSection section = builder.Configuration.GetSection("Catan");
var options = section.Get<ServerOptions>() ?? new ServerOptions();
// The binder appends configured array items to the defaults; configured personas should replace them.
BotPersona[] personas = section.GetSection("Bots:Personas").Get<BotPersona[]>();
if (personas != null && personas.Length > 0) options.Bots.Personas = personas;

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

app.Run();

/// <summary>Exposed so integration tests can host the real pipeline in memory.</summary>
public partial class Program { }
