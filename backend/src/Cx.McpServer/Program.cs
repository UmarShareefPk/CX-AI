using Cx.Ai;
using Cx.Core;
using Cx.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddCxSharedSettings();

// stdout is the MCP protocol channel: every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddCxCore(builder.Configuration).AddCxAi(builder.Configuration);
builder.Services.AddOptions<ScopeOptions>()
    .Bind(builder.Configuration.GetSection(ScopeOptions.Section))
    .Validate(o => o.Role is not null, "Cx:Scope:Role must be set explicitly (Admin or Dealer); there is no default.")
    .Validate(o => o.Role == Cx.Core.Domain.UserRole.Admin || !string.IsNullOrWhiteSpace(o.DealerId), "A Dealer scope requires Cx:Scope:DealerId.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ScopeOptions>>().Value.ToDataScope());

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
