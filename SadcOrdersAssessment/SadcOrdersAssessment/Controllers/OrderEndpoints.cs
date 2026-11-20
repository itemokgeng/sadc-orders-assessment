using Core.DTOs;
using Core.Exceptions;
using Core.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SadcOrdersAssessment.Helpers;
using System.Net.Http.Headers;
using System.Text;

namespace SadcOrdersAssessment.Controllers
{
    public static class OrderEndpoints
    {
        public const string IdempotencyKeyHeader = "Idempotency-Key";
        public const string IdempotentReplayedHeader = "Idempotent-Replayed";
        private const int MaxIdempotencyKeyLength = 100;

        public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/orders").WithTags("Orders");

            group.MapPost(string.Empty, CreateAsync)
                .RequireAuthorization(AuthPolicies.OrdersWrite)
                .Validate<CreateOrderRequest>()
                .WithName("CreateOrder")
                .WithSummary("Create an order. The total is computed on the server and OrderCreated is published through the outbox.");

            group.MapGet("/{id:guid}", GetAsync)
                .RequireAuthorization(AuthPolicies.OrdersRead)
                .WithName("GetOrder")
                .WithSummary("Get an order with its line items. Returns an ETag and answers If-None-Match with 304.")
                .Produces<OrderDto>()
                .Produces(StatusCodes.Status304NotModified)
                .ProducesProblem(StatusCodes.Status404NotFound);

            group.MapGet(string.Empty, ListAsync)
                .RequireAuthorization(AuthPolicies.OrdersRead)
                .Validate<OrderListQuery>()
                .WithName("ListOrders")
                .WithSummary("Filter by customerId and status; sort by createdAt, -createdAt, totalAmount or -totalAmount; pageSize ≤ 100.");

            group.MapPut("/{id:guid}/status", UpdateStatusAsync)
                .RequireAuthorization(AuthPolicies.OrdersWrite)
                .Validate<UpdateOrderStatusRequest>()
                .WithName("UpdateOrderStatus")
                .WithSummary("Change status (idempotent: the Idempotency-Key header is required; optional If-Match).")
                .Produces<OrderDto>()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status412PreconditionFailed)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

            return app;
        }

        private static async Task<Created<OrderDto>> CreateAsync(CreateOrderRequest request, IOrderService orders, HttpContext http, CancellationToken ct)
        {
            var created = await orders.CreateAsync(request, ct);
            http.Response.Headers.ETag = created.ETag;
            return TypedResults.Created($"/api/orders/{created.Order.Id}", created.Order);
        }

        private static async Task<IResult> GetAsync(Guid id, IOrderService orders, HttpContext http, CancellationToken ct)
        {
            var (order, etag) = await orders.GetAsync(id, ct);

            http.Response.Headers.ETag = etag;
            // The browser may cache this, but must check with the server (If-None-Match) before reusing it.
            http.Response.Headers.CacheControl = "private, no-cache";

            var ifNoneMatch = http.Request.GetTypedHeaders().IfNoneMatch;
            if (ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Tag.ToString() == etag))
            {
                return TypedResults.StatusCode(StatusCodes.Status304NotModified);
            }

            return TypedResults.Ok(order);
        }

        private static async Task<Ok<PagedResult<OrderSummaryDto>>> ListAsync([AsParameters] OrderListQuery query, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.ListAsync(query, ct));

        private static async Task<IResult> UpdateStatusAsync(
            Guid id,
            UpdateOrderStatusRequest request,
            [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
            HttpContext http,
            IOrderService orders,
            IAuthorizationService authorization,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
            {
                throw new RequestValidationException(
                    IdempotencyKeyHeader,
                    $"The {IdempotencyKeyHeader} header is required (1-{MaxIdempotencyKeyLength} characters, e.g. a UUID). Reuse the same key when retrying the same change.");
            }

            var ifMatch = http.Request.GetTypedHeaders().IfMatch.FirstOrDefault()?.Tag.ToString();
            var isAdmin = (await authorization.AuthorizeAsync(http.User, AuthPolicies.OrdersAdmin)).Succeeded;

            var result = await orders.UpdateStatusAsync(
                new UpdateOrderStatusCommand(id, request.Status!, idempotencyKey.Trim(), ifMatch, isAdmin),
                ct);

            if (result.ETag is not null)
            {
                http.Response.Headers.ETag = result.ETag;
            }

            if (result.Replayed)
            {
                http.Response.Headers[IdempotentReplayedHeader] = "true";
            }

            return Results.Content(result.ResponseBody, "application/json", Encoding.UTF8, result.StatusCode);
        }
    }
}