using Core.Interface;
using DataLayer.Context;
using DataLayer.Messaging;
using DataLayer.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


namespace DataLayer;

public static class DependencyInjection
{
    public const string ConnectionStringName = "OrdersDb";

    /// <summary>DbContext, repositories and the seeder.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        }

        services.AddDbContext<OrdersDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                // Retries transient Azure SQL / container start-up errors. Code that opens its own
                // transaction must go through the execution strategy.
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            }));

        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<DataSeeder>();
        return services;
    }

    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        return services;
    }

    /// <summary>Outbox processor plus, optionally, the background dispatcher (turned off in tests).</summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services, bool runDispatcher)
    {
        services.AddScoped<OutboxProcessor>();
        if (runDispatcher)
        {
            services.AddHostedService<OutboxDispatcher>();
        }

        return services;
    }
}
