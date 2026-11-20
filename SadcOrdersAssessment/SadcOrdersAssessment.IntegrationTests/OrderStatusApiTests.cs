using System.Net;
using System.Net.Http.Json;
using Core.DTOs;
using DataLayer.Context;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SadcOrders.IntegrationTests.Infrastructure;

namespace SadcOrders.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrderStatusApiTests
{
    private readonly ApiFactory _factory;

    public OrderStatusApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, OrderDto Order)> NewPendingOrderAsync()
    {
        var client = _factory.Writer();
        var customer = await TestData.CreateCustomerAsync(client);
        return (client, await TestData.CreateOrderAsync(client, customer.Id));
    }

    [Fact]
    public async Task Requires_an_Idempotency_Key_header()
    {
        var (client, order) = await NewPendingOrderAsync();

        var response = await TestData.PutStatusAsync(client, order.Id, "Paid", idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        TestData.ErrorKeys(await TestData.ProblemAsync(response)).Should().BeEquivalentTo("Idempotency-Key");
    }

    [Fact]
    public async Task Retrying_with_the_same_key_replays_the_first_response_without_reapplying_it()
    {
        var (client, order) = await NewPendingOrderAsync();
        var key = Guid.NewGuid().ToString();

        var first = await TestData.PutStatusAsync(client, order.Id, "Paid", key);
        var firstBody = await first.Content.ReadAsStringAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK, firstBody);
        first.Headers.Contains("Idempotent-Replayed").Should().BeFalse();

        var retry = await TestData.PutStatusAsync(client, order.Id, "Paid", key);

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        retry.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await retry.Content.ReadAsStringAsync()).Should().Be(firstBody);

        var current = await client.GetFromJsonAsync<OrderDto>($"/api/orders/{order.Id}");
        current!.Status.Should().Be("Paid");
        current.AllowedTransitions.Should().BeEquivalentTo("Fulfilled", "Cancelled");
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_apply_the_change_exactly_once()
    {
        var (client, order) = await NewPendingOrderAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TestData.PutStatusAsync(client, order.Id, "Paid", key)));

        // One request applies the change. The others either find its idempotency record or lose the
        // race (stale rowversion, PK clash) and replay the winner's response, so every caller gets 200.
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).Should().Be(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.IdempotencyRecords.CountAsync(r => r.Key.Contains(key))).Should().Be(1);
        (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_change_returns_422()
    {
        var (client, order) = await NewPendingOrderAsync();
        var key = Guid.NewGuid().ToString();
        (await TestData.PutStatusAsync(client, order.Id, "Paid", key)).EnsureSuccessStatusCode();

        var response = await TestData.PutStatusAsync(client, order.Id, "Cancelled", key);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await TestData.ProblemAsync(response)).GetProperty("code").GetString().Should().Be("idempotency_key_reused");
    }

    [Fact]
    public async Task Invalid_transition_returns_409_with_the_allowed_transitions()
    {
        var (client, order) = await NewPendingOrderAsync();

        var response = await TestData.PutStatusAsync(client, order.Id, "Fulfilled", Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await TestData.ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("invalid_status_transition");
        problem.GetProperty("currentStatus").GetString().Should().Be("Pending");
        problem.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("Paid", "Cancelled");
    }

    [Fact]
    public async Task Unknown_status_is_a_validation_error()
    {
        var (client, order) = await NewPendingOrderAsync();

        var response = await TestData.PutStatusAsync(client, order.Id, "Shipped", Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        TestData.ErrorKeys(await TestData.ProblemAsync(response)).Should().BeEquivalentTo("status");
    }

    [Fact]
    public async Task Cancelling_a_paid_order_requires_the_admin_role()
    {
        var (client, order) = await NewPendingOrderAsync();
        (await TestData.PutStatusAsync(client, order.Id, "Paid", Guid.NewGuid().ToString())).EnsureSuccessStatusCode();

        var asWriter = await TestData.PutStatusAsync(client, order.Id, "Cancelled", Guid.NewGuid().ToString());
        asWriter.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var asAdmin = await TestData.PutStatusAsync(_factory.Admin(), order.Id, "Cancelled", Guid.NewGuid().ToString());
        asAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readers_cannot_change_status()
    {
        var (_, order) = await NewPendingOrderAsync();

        var response = await TestData.PutStatusAsync(_factory.Reader(), order.Id, "Paid", Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stale_If_Match_returns_412()
    {
        var (client, order) = await NewPendingOrderAsync();
        var original = (await client.GetAsync($"/api/orders/{order.Id}")).Headers.ETag!.Tag;
        (await TestData.PutStatusAsync(client, order.Id, "Paid", Guid.NewGuid().ToString())).EnsureSuccessStatusCode();

        var response = await TestData.PutStatusAsync(client, order.Id, "Fulfilled", Guid.NewGuid().ToString(), ifMatch: original);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task RowVersion_detects_a_lost_update()
    {
        var (_, order) = await NewPendingOrderAsync();

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var copyA = await dbA.Orders.SingleAsync(o => o.Id == order.Id);
        var copyB = await dbB.Orders.SingleAsync(o => o.Id == order.Id);

        copyA.TransitionTo(OrderStatus.Paid, DateTime.UtcNow);
        await dbA.SaveChangesAsync();

        copyB.TransitionTo(OrderStatus.Cancelled, DateTime.UtcNow);
        var act = () => dbB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
