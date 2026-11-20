using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Core.DTOs;
using DataLayer.Context;
using DataLayer.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SadcOrders.IntegrationTests.Infrastructure;

namespace SadcOrders.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrdersApiTests
{
    private readonly ApiFactory _factory;

    public OrdersApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_computes_the_total_on_the_server_and_writes_OrderCreated_to_the_outbox()
    {
        var client = _factory.Writer();
        var customer = await TestData.CreateCustomerAsync(client, "ZW");

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(
            customer.Id,
            "usd",
            new[] { new CreateOrderLineRequest("solar-lamp", 3, 24.99m), new CreateOrderLineRequest("SEED-PACK", 10, 3.10m) }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.ETag.Should().NotBeNull();
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>())!;
        order.Status.Should().Be("Pending");
        order.CurrencyCode.Should().Be("USD");
        order.TotalAmount.Should().Be(105.97m);
        order.LineItems.Should().HaveCount(2);
        order.LineItems.Should().Contain(li => li.ProductSku == "SOLAR-LAMP" && li.LineTotal == 74.97m);
        order.AllowedTransitions.Should().BeEquivalentTo("Paid", "Cancelled");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Payload.Contains(order.Id.ToString()));
        outbox.Type.Should().Be(OrderCreated.MessageType);
        outbox.ProcessedAt.Should().BeNull();
        outbox.CorrelationId.Should().Be(response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Outbox_processor_publishes_pending_events_once_and_marks_them_processed()
    {
        var client = _factory.Writer();
        var customer = await TestData.CreateCustomerAsync(client);
        var order = await TestData.CreateOrderAsync(client, customer.Id);

        await DrainOutboxAsync();
        await DrainOutboxAsync(); // a second run must not publish the same event again

        var published = _factory.Publisher.Published.Where(m => m.Body.Contains(order.Id.ToString())).ToList();
        published.Should().ContainSingle();
        published[0].RoutingKey.Should().Be(RabbitMqTopology.OrderCreatedRoutingKey);

        var message = JsonSerializer.Deserialize<OrderCreated>(published[0].Body, MessageSerializer.Options)!;
        message.OrderId.Should().Be(order.Id);
        message.TotalAmount.Should().Be(order.TotalAmount);
        message.MessageId.Should().Be(published[0].MessageId);

        await using var verify = _factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.MessageId)).ProcessedAt.Should().NotBeNull();
    }

    private async Task DrainOutboxAsync()
    {
        int published;
        do
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            published = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
        }
        while (published == OutboxProcessor.BatchSize);
    }

    [Fact]
    public async Task Create_rejects_a_currency_not_permitted_for_the_customers_country()
    {
        var client = _factory.Writer();
        var customer = await TestData.CreateCustomerAsync(client, "BW");

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(customer.Id, "ZAR", new[] { new CreateOrderLineRequest("SKU", 1, 1m) }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await TestData.ProblemAsync(response);
        TestData.ErrorKeys(problem).Should().BeEquivalentTo("currencyCode");
        problem.GetProperty("errors").GetProperty("currencyCode")[0].GetString().Should().Contain("BWP");
    }

    [Fact]
    public async Task Create_rejects_an_unknown_customer_and_invalid_lines()
    {
        var client = _factory.Writer();

        var unknown = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(), "ZAR", new[] { new CreateOrderLineRequest("SKU", 1, 1m) }));
        TestData.ErrorKeys(await TestData.ProblemAsync(unknown)).Should().BeEquivalentTo("customerId");

        var badLine = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(), "ZAR", new[] { new CreateOrderLineRequest("SKU", 0, 1m) }));
        TestData.ErrorKeys(await TestData.ProblemAsync(badLine)).Should().BeEquivalentTo("lineItems[0].quantity");
    }

    [Fact]
    public async Task Get_returns_an_ETag_and_304_when_the_order_is_unchanged()
    {
        var client = _factory.Writer();
        var customer = await TestData.CreateCustomerAsync(client);
        var order = await TestData.CreateOrderAsync(client, customer.Id);

        var first = await client.GetAsync($"/api/orders/{order.Id}");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag!;

        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.Id}");
        conditional.Headers.IfNoneMatch.Add(etag);
        (await client.SendAsync(conditional)).StatusCode.Should().Be(HttpStatusCode.NotModified);

        // After a change the old ETag no longer matches.
        (await TestData.PutStatusAsync(client, order.Id, "Paid", Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        var afterChange = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.Id}");
        afterChange.Headers.IfNoneMatch.Add(etag);
        var changed = await client.SendAsync(afterChange);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        changed.Headers.ETag!.Tag.Should().NotBe(etag.Tag);
    }

    [Fact]
    public async Task List_filters_by_customer_and_status_and_sorts_by_total()
    {
        var client = _factory.Writer();
        var customer = await TestData.CreateCustomerAsync(client, "MW");
        var small = await TestData.CreateOrderAsync(client, customer.Id, "MWK", new CreateOrderLineRequest("A", 1, 100m));
        var large = await TestData.CreateOrderAsync(client, customer.Id, "MWK", new CreateOrderLineRequest("B", 1, 900m));
        var medium = await TestData.CreateOrderAsync(client, customer.Id, "MWK", new CreateOrderLineRequest("C", 1, 500m));
        (await TestData.PutStatusAsync(client, medium.Id, "Paid", Guid.NewGuid().ToString())).EnsureSuccessStatusCode();

        var byTotal = await client.GetFromJsonAsync<PagedResult<OrderSummaryDto>>($"/api/orders?customerId={customer.Id}&sort=-totalAmount");
        byTotal!.Items.Select(o => o.Id).Should().Equal(large.Id, medium.Id, small.Id);
        byTotal.Items.Should().OnlyContain(o => o.CustomerName == customer.Name && o.LineItemCount == 1);

        var paid = await client.GetFromJsonAsync<PagedResult<OrderSummaryDto>>($"/api/orders?customerId={customer.Id}&status=paid");
        paid!.TotalCount.Should().Be(1);
        paid.Items.Single().Id.Should().Be(medium.Id);
    }

    [Fact]
    public async Task List_rejects_unknown_sort_and_status()
    {
        var response = await _factory.Reader().GetAsync("/api/orders?sort=name&status=Shipped&pageSize=500");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        TestData.ErrorKeys(await TestData.ProblemAsync(response)).Should().BeEquivalentTo("sort", "status", "pageSize");
    }

    [Fact]
    public async Task Reference_endpoint_lists_SADC_countries_without_auth()
    {
        var countries = await _factory.CreateClient().GetFromJsonAsync<List<SadcCountryDto>>("/api/reference/sadc-countries");

        countries.Should().HaveCount(16);
        countries!.Single(c => c.Code == "ZW").Currencies.Should().Contain(new[] { "ZWL", "USD" });
        countries.Single(c => c.Code == "NA").IsCommonMonetaryArea.Should().BeTrue();
    }

    [Fact]
    public async Task Health_endpoints_respond()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/healthz")).StatusCode.Should().Be(HttpStatusCode.OK);

        // RabbitMQ isn't running in the test environment, so readiness must report it.
        (await client.GetAsync("/readiness")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Responses_carry_secure_default_headers()
    {
        var response = await _factory.CreateClient().GetAsync("/api/reference/sadc-countries");

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
    }

    [Fact]
    public async Task Correlation_id_from_the_client_is_echoed_back()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/reference/sadc-countries");
        request.Headers.Add("X-Correlation-ID", "test-corr-123");

        var response = await _factory.CreateClient().SendAsync(request);

        response.Headers.GetValues("X-Correlation-ID").Single().Should().Be("test-corr-123");
    }

    [Fact]
    public async Task Metrics_endpoint_counts_requests_by_route_template()
    {
        var client = _factory.Reader();
        await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        var metrics = await client.GetFromJsonAsync<JsonElement>("/metrics");

        metrics.GetProperty("routes").TryGetProperty("GET /api/orders/{id:guid}", out var route).Should().BeTrue();
        route.GetProperty("count").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Malformed_json_returns_problem_details_not_500()
    {
        var content = new StringContent("{ not json", MediaTypeHeaderValue.Parse("application/json"));

        var response = await _factory.Writer().PostAsync("/api/orders", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestData.ProblemAsync(response)).GetProperty("code").GetString().Should().Be("bad_request");
    }
}
