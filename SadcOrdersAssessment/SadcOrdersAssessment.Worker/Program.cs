using DataLayer;
using SadcOrders.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

// ---- Logging to a rolling file (Serilog), as well as the console above. ----
// One file per day, e.g. logs/worker-20261120.log, kept for 14 days (a new file also starts after 10 MB).
// Serilog is added as just another logging provider, so the "Logging:LogLevel" settings still decide
// what gets logged, and the scopes (CorrelationId, MessageId, OrderId) show up on every line.
if (builder.Configuration.GetValue("FileLogging:Enabled", true))
{
    var logFilePath = builder.Configuration["FileLogging:Path"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "logs", "worker-.log");

    var fileLogger = new LoggerConfiguration()
        .MinimumLevel.Verbose()
        .WriteTo.File(
            logFilePath,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1))
        .CreateLogger();

    builder.Logging.AddSerilog(fileLogger, dispose: true);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddScoped<OrderCreatedHandler>();
builder.Services.AddHostedService<OrderCreatedConsumer>();

var host = builder.Build();
await host.RunAsync();
