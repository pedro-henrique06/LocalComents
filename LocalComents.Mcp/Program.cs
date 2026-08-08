using LocalComents.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A stdio MCP server speaks JSON-RPC over stdout, so every diagnostic must go to stderr.
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// The shared CommentStore reports failures through LocalComentsLog (Trace); surface them on stderr
// so a misconfigured storage path is diagnosable instead of looking like "no comments".
System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener(useErrorStream: true));

CommentSource.Initialize(args);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();
