using Conquer.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateSlimBuilder(args);

IConfigurationSection section = builder.Configuration.GetSection("Conquer");
var options = section.Get<ServerOptions>() ?? new ServerOptions();
// The binder appends configured array items to the defaults; configured personas should replace them.
BotPersona[] personas = section.GetSection("Bots:Personas").Get<BotPersona[]>();
if (personas != null && personas.Length > 0) options.Bots.Personas = personas;

ServerApp.Build(builder, options).Run();

/// <summary>Exposed so integration tests can host the real pipeline in memory.</summary>
public partial class Program { }
