using Core.DTOs;
using Core.Interface;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace SadcOrders.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API in memory against a real SQL Server in Docker (Testcontainers). The schema is
/// built by the actual EF migrations (MigrateOnStartup), so these tests also check that the
/// migrations apply cleanly to an empty database. RabbitMQ is swapped for an in-memory publisher;
/// the outbox to publisher path is tested by calling OutboxProcessor directly.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public CapturingPublisher Publisher { get; } = new();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "SadcOrdersTests",
        }.ConnectionString;

        // Environment variables are read when WebApplication.CreateBuilder runs, so these reach the
        // code in Program.cs before Build(). (Configuration added in ConfigureWebHost may arrive too late.)
        Environment.SetEnvironmentVariable("ConnectionStrings__OrdersDb", ConnectionString);
        Environment.SetEnvironmentVariable("Auth__Mode", "Mock");
        Environment.SetEnvironmentVariable("Auth__Mock__SigningKey", "integration-tests-signing-key-0123456789abcdef");
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");
        Environment.SetEnvironmentVariable("Outbox__DispatcherEnabled", "false");
        Environment.SetEnvironmentVariable("FileLogging__Enabled", "false"); // no log files from test runs

        // Point at a host that can't exist, so readiness is deterministic even if the developer has
        // RabbitMQ running locally from docker compose.
        Environment.SetEnvironmentVariable("RabbitMq__HostName", "rabbitmq.invalid");

        // Build the host now, so migrations run once before any test.
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMessagePublisher>();
            services.AddSingleton<IMessagePublisher>(Publisher);
        });
    }

    public HttpClient CreateClientWithRoles(params string[] roles)
    {
        var client = CreateClient();
        if (roles.Length > 0)
        {
            var (token, _) = Services.GetRequiredService<MockTokenIssuer>().Issue(roles, "Integration Test");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public HttpClient Writer() => CreateClientWithRoles(AuthPolicies.OrdersWrite);

    public HttpClient Reader() => CreateClientWithRoles(AuthPolicies.OrdersRead);

    public HttpClient Admin() => CreateClientWithRoles(AuthPolicies.OrdersAdmin);

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

public sealed class CapturingPublisher : IMessagePublisher
{
    public ConcurrentQueue<OutgoingMessage> Published { get; } = new();

    public Task PublishAsync(OutgoingMessage message, CancellationToken ct)
    {
        Published.Enqueue(message);
        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
