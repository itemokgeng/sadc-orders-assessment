using Core.DTOs;
using DataLayer.Context;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SadcOrders.IntegrationTests.Infrastructure;

namespace SadcOrders.IntegrationTests;

/// <summary>
/// Runs the worker's handler against the same SQL Server. These tests cover the consumer's
/// idempotency, which is what makes at-least-once delivery safe.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkerHandlerTests
{
    private readonly ApiFactory _factory;

    public WorkerHandlerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static OrderCreatedHandler CreateHandler(OrdersDbContext db, double failureRate = 0) =>
        new(db, TimeProvider.System, Options.Create(new WorkerOptions { SimulatedProcessingDelayMs = 0, SimulatedFailureRate = failureRate }), NullLogger<OrderCreatedHandler>.Instance);

    private static OrderCreated MessageFor(OrderDto order) =>
        new(Guid.NewGuid(), order.Id, order.CustomerId, order.CurrencyCode, order.TotalAmount, order.CreatedAt, "test-correlation");

    private async Task<OrderDto> NewOrderAsync()
    {
        var client = _factory.Writer();
        var customer = await TestData.CreateCustomerAsync(client, "TZ");
        return await TestData.CreateOrderAsync(client, customer.Id, "TZS");
    }

    [Fact]
    public async Task Fulfils_a_pending_order_and_skips_a_redelivered_message()
    {
        var order = await NewOrderAsync();
        var message = MessageFor(order);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var outcome = await CreateHandler(scope.ServiceProvider.GetRequiredService<OrdersDbContext>()).HandleAsync(message, CancellationToken.None);
            outcome.Should().Be(HandleOutcome.Fulfilled);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // Same MessageId again, as after a broker redelivery.
            var outcome = await CreateHandler(scope.ServiceProvider.GetRequiredService<OrdersDbContext>()).HandleAsync(message, CancellationToken.None);
            outcome.Should().Be(HandleOutcome.DuplicateSkipped);
        }

        await using var verify = _factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(OrderStatus.Fulfilled);
        (await db.ProcessedMessages.CountAsync(m => m.MessageId == message.MessageId)).Should().Be(1);
    }

    [Fact]
    public async Task Leaves_an_order_cancelled_before_processing_alone()
    {
        var order = await NewOrderAsync();
        (await TestData.PutStatusAsync(_factory.Writer(), order.Id, "Cancelled", Guid.NewGuid().ToString())).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var outcome = await CreateHandler(db).HandleAsync(MessageFor(order), CancellationToken.None);

        outcome.Should().Be(HandleOutcome.AlreadyFinal);
        (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task A_failed_attempt_changes_nothing_so_the_retry_starts_clean()
    {
        var order = await NewOrderAsync();
        var message = MessageFor(order);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var act = () => CreateHandler(scope.ServiceProvider.GetRequiredService<OrdersDbContext>(), failureRate: 1).HandleAsync(message, CancellationToken.None);
            await act.Should().ThrowAsync<SimulatedProcessingException>();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(OrderStatus.Pending);
            (await db.ProcessedMessages.AnyAsync(m => m.MessageId == message.MessageId)).Should().BeFalse();

            (await CreateHandler(db).HandleAsync(message, CancellationToken.None)).Should().Be(HandleOutcome.Fulfilled);
        }
    }
}
