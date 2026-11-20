using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.DTOs;

namespace SadcOrders.IntegrationTests.Infrastructure;

public static class TestData
{
    public static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string countryCode = "ZA", string? name = null)
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(name ?? $"Customer {unique}", $"{unique}@example.test", countryCode));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    public static async Task<OrderDto> CreateOrderAsync(HttpClient client, Guid customerId, string currency = "ZAR", params CreateOrderLineRequest[] lines)
    {
        if (lines.Length == 0)
        {
            lines = new[] { new CreateOrderLineRequest("SKU-1", 2, 50.25m) };
        }

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(customerId, currency, lines));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    public static async Task<HttpResponseMessage> PutStatusAsync(HttpClient client, Guid orderId, string status, string? idempotencyKey, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/orders/{orderId}/status")
        {
            Content = JsonContent.Create(new UpdateOrderStatusRequest(status)),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Parses a Problem Details body.</summary>
    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    public static IReadOnlyCollection<string> ErrorKeys(JsonElement problem) =>
        problem.TryGetProperty("errors", out var errors)
            ? errors.EnumerateObject().Select(p => p.Name).ToList()
            : Array.Empty<string>();
}
