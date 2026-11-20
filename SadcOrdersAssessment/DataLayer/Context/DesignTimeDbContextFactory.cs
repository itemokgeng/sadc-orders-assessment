using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataLayer.Context;

/// <summary>
/// Used only by the `dotnet ef` tools, so migrations add/script/list don't need to start the API
/// host or have RabbitMQ running. Override the target with the SADC_ORDERS_DB environment variable.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    private const string DefaultConnection =
        "Server=localhost,1433;Database=SadcOrders;User Id=sa;Password=Your_strong_Passw0rd!;TrustServerCertificate=True";

    public OrdersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SADC_ORDERS_DB") ?? DefaultConnection;
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new OrdersDbContext(options);
    }
}
