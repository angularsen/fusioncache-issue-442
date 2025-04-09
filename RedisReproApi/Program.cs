Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    // .MinimumLevel.Override("ZiggyCreatures.Caching.Fusion", LogEventLevel.Warning) // Information includes successful cache/backplane operations
    .Enrich.WithProperty("Application", "RedisRepro")
    .Enrich.WithProperty("ApplicationInstance", Environment.GetEnvironmentVariable("APP_INSTANCE") ?? "")
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(theme: TemplateTheme.Code), LogEventLevel.Warning) // Only error/warning in console, use Seq to view all events.
    .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
    .CreateLogger();

using var listener = new ActivityListenerConfiguration()
    .Instrument.AspNetCoreRequests()
    .TraceToSharedLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();
var app = builder.Build();

ReproEndpoint.MapReproApi(app);

try
{
    app.Run();
}
finally
{
    await Log.CloseAndFlushAsync();
}