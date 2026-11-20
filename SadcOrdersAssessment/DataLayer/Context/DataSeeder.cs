using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataLayer.Context;

/// <summary>
/// Seeds demo data: customers spread over all 16 SADC states, and orders in each customer's allowed
/// currencies with a realistic spread of statuses over the last 180 days. The fixed random seed makes
/// runs repeatable. Seeded orders don't publish OrderCreated: they are history, not new business.
/// </summary>
public class DataSeeder
{
    private static readonly string[] FirstNames =
    {
        "Thabo", "Naledi", "Kagiso", "Lerato", "Tendai", "Chipo", "Amara", "Baraka", "Neema", "Tsepo",
        "Mpho", "Lindiwe", "Farai", "Rudo", "Kabelo", "Zanele", "Joao", "Ana", "Rivo", "Faly",
    };

    private static readonly string[] LastNames =
    {
        "Mokoena", "Dlamini", "Ndlovu", "Moyo", "Banda", "Phiri", "Mensah", "Mwangi", "Sithole", "Nkosi",
        "Molefe", "Chikwanha", "Mabaso", "Santos", "Rakoto", "Kamau", "Tembo", "Mutasa", "Mahlangu", "Khumalo",
    };

    private static readonly (string Sku, decimal BasePriceZar)[] Products =
    {
        ("MAIZE-25KG", 289.99m), ("SOLAR-LAMP", 449.00m), ("WATER-TANK-1K", 2199.00m), ("CEMENT-50KG", 119.50m),
        ("FERTILISER-50KG", 899.00m), ("SEED-PACK", 59.99m), ("PHONE-BASIC", 399.00m), ("BICYCLE", 2899.00m),
    };

    // Rough ZAR cross rates so seeded prices look plausible in each currency. Demo data only.
    private static readonly Dictionary<string, decimal> ApproxPerZar = new()
    {
        ["ZAR"] = 1m,
        ["NAD"] = 1m,
        ["LSL"] = 1m,
        ["SZL"] = 1m,
        ["BWP"] = 0.74m,
        ["AOA"] = 50m,
        ["KMF"] = 25m,
        ["CDF"] = 155m,
        ["MGA"] = 250m,
        ["MWK"] = 95m,
        ["MUR"] = 2.5m,
        ["MZN"] = 3.5m,
        ["SCR"] = 0.75m,
        ["TZS"] = 145m,
        ["ZMW"] = 1.5m,
        ["ZWL"] = 17m,
        ["USD"] = 0.055m,
        ["ZWG"] = 1.5m,
    };

    private readonly OrdersDbContext _db;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(OrdersDbContext db, ILogger<DataSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedAsync(int customerCount, int orderCount, CancellationToken ct = default)
    {
        if (await _db.Customers.AnyAsync(ct))
        {
            _logger.LogInformation("Seed skipped: database already has customers");
            return;
        }

        var random = new Random(20261116);
        var now = DateTime.UtcNow;
        var countries = SadcCurrencyRules.GetCountries(allowRandInCommonMonetaryArea: false);

        var customers = new List<Customer>(customerCount);
        for (var i = 0; i < customerCount; i++)
        {
            var country = countries[i % countries.Count];
            var first = FirstNames[random.Next(FirstNames.Length)];
            var last = LastNames[random.Next(LastNames.Length)];
            customers.Add(Customer.Create(
                $"{first} {last}",
                $"{first}.{last}.{i:D4}@example.{country.Code.ToLowerInvariant()}",
                country.Code,
                now.AddDays(-random.Next(181, 365))));
        }

        _db.Customers.AddRange(customers);
        await _db.SaveChangesAsync(ct);

        const int batchSize = 250;
        for (var i = 0; i < orderCount; i++)
        {
            var customer = customers[random.Next(customers.Count)];
            var allowed = SadcCurrencyRules.AllowedCurrencies(customer.CountryCode, allowRandInCommonMonetaryArea: false);
            var currency = allowed[random.Next(allowed.Count)];
            var lines = Enumerable.Range(0, random.Next(1, 5))
                .Select(_ =>
                {
                    var product = Products[random.Next(Products.Length)];
                    var price = decimal.Round(product.BasePriceZar * ApproxPerZar[currency], 2);
                    return new NewOrderLine(product.Sku, random.Next(1, 10), price);
                })
                .ToList();

            var createdAt = now.AddMinutes(-random.Next(0, 180 * 24 * 60));
            var order = Order.Create(customer.Id, currency, lines, createdAt);

            // Roughly 15% Pending, 15% Paid, 60% Fulfilled, 10% Cancelled.
            var roll = random.Next(100);
            var updatedAt = createdAt.AddHours(random.Next(1, 48));
            if (roll >= 15 && roll < 90)
            {
                order.TransitionTo(OrderStatus.Paid, updatedAt);
                if (roll >= 30)
                {
                    order.TransitionTo(OrderStatus.Fulfilled, updatedAt);
                }
            }
            else if (roll >= 90)
            {
                order.TransitionTo(OrderStatus.Cancelled, updatedAt);
            }

            _db.Orders.Add(order);

            if ((i + 1) % batchSize == 0)
            {
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
            }
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        _logger.LogInformation("Seeded {CustomerCount} customers and {OrderCount} orders", customerCount, orderCount);
    }
}
