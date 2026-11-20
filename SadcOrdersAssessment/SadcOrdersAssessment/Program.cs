using Core.Interface;
using DataLayer;
using DataLayer.Context;
using FluentValidation;
using LogicLayer;
using LogicLayer.RepositoryService;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SadcOrders.Services;
using SadcOrdersAssessment.Controllers;
using SadcOrdersAssessment.Middleware;
using SadcOrdersAssessment.Services;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging: structured JSON on stdout. Scopes carry CorrelationId on every log line. ----
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

// ---- Logging to a rolling file (Serilog), as well as the console above. ----
// One file per day, e.g. logs/api-20261120.log, kept for 14 days (a new file also starts after 10 MB).
// Serilog is added as just another logging provider, so the "Logging:LogLevel" settings still decide
// what gets logged, and the scopes (CorrelationId, MessageId, OrderId) show up on every line.
if (builder.Configuration.GetValue("FileLogging:Enabled", true))
{
    var logFilePath = builder.Configuration["FileLogging:Path"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "logs", "api-.log");

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

// ---- Core services ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICorrelationIdAccessor, HttpCorrelationIdAccessor>();
builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Return binding failures (malformed JSON, a bad GUID in the query) as Problem Details in every
// environment, rather than an empty 400 outside Development.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddOutbox(runDispatcher: builder.Configuration.GetValue("Outbox:DispatcherEnabled", true));

builder.Services.AddLogicLayer(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

// ---- Errors: one RFC 7807 contract everywhere ----
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});

// ---- Auth (Entra or mock) ----
builder.Services.AddApiAuth(builder.Configuration, builder.Environment);
var mockAuth = AuthSetup.IsMockMode(builder.Configuration);

// ---- Health, metrics, CORS, OpenAPI ----
builder.Services.AddSingleton<RequestMetrics>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrdersDbContext>("sqlserver", tags: new[] { SystemEndpoints.ReadyTag })
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: new[] { SystemEndpoints.ReadyTag }, timeout: TimeSpan.FromSeconds(5));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT")
    .WithExposedHeaders("ETag", CorrelationIdMiddleware.HeaderName, OrderEndpoints.IdempotentReplayedHeader, "Location")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SADC Order Management API",
        Version = "v1",
        Description = "Customers and orders for SADC member states. In mock auth mode, get a token from POST /api/dev/token and click Authorize.",
    });
    var bearer = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Microsoft Entra access token (or a mock token from /api/dev/token).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", bearer);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = Array.Empty<string>() });
});

var app = builder.Build();

// ---- Pipeline (order matters) ----
app.UseMiddleware<CorrelationIdMiddleware>();   // first, so every later log line has the correlation id
app.UseMiddleware<RequestMetricsMiddleware>();  // outside the exception handler, so it sees final status codes
app.UseExceptionHandler();                      // exceptions -> Problem Details
app.UseStatusCodePages();                       // empty 401/403/404 -> Problem Details
app.Use(async (context, next) =>
{
    // Secure default headers. This is a JSON API, so lock down CSP, except on the Swagger UI.
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    if (!context.Request.Path.StartsWithSegments("/swagger"))
    {
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }

    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "SADC Orders v1"));
}

app.UseRouting();
app.Use(RequestMetricsMiddleware.CaptureRoute); // after routing, so the route template is known
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapSystemEndpoints(mockAuth);
app.MapCustomerEndpoints();
app.MapOrderEndpoints();

// ---- Database lifecycle (local/dev convenience; see README for production) ----
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.MigrateAsync();

    if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync(
            app.Configuration.GetValue("Seed:Customers", 200),
            app.Configuration.GetValue("Seed:Orders", 1200));
    }
}

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory can start the API in integration tests.</summary>
public partial class Program
{
}
