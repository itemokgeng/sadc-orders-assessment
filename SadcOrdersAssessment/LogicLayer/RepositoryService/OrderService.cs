using Core.DTOs;
using Core.Exceptions;
using Core.Interface;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogicLayer.RepositoryService
{
    public sealed class OrderService : IOrderService
    {
        /// <summary>Same settings as the API's JSON output (camelCase), so stored idempotent responses match live ones.</summary>
        private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

        private readonly IOrderRepository _orders;
        private readonly ICustomerRepository _customers;
        private readonly IIdempotencyRepository _idempotency;
        private readonly TimeProvider _time;
        private readonly ICorrelationIdAccessor _correlation;
        private readonly SadcOptions _sadc;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            IOrderRepository orders,
            ICustomerRepository customers,
            IIdempotencyRepository idempotency,
            TimeProvider time,
            ICorrelationIdAccessor correlation,
            IOptions<SadcOptions> sadc,
            ILogger<OrderService> logger)
        {
            _orders = orders;
            _customers = customers;
            _idempotency = idempotency;
            _time = time;
            _correlation = correlation;
            _sadc = sadc.Value;
            _logger = logger;
        }

        public async Task<OrderWithETag> CreateAsync(CreateOrderRequest request, CancellationToken ct)
        {
            var customerId = request.CustomerId!.Value;
            var customer = await _customers.GetSummaryAsync(customerId, ct)
                ?? throw new RequestValidationException("customerId", $"Customer '{customerId}' does not exist.");

            var currency = SadcCurrencyRules.Normalize(request.CurrencyCode!);
            if (!SadcCurrencyRules.IsCurrencyAllowed(customer.CountryCode, currency, _sadc.AllowRandInCommonMonetaryArea))
            {
                var allowed = SadcCurrencyRules.AllowedCurrencies(customer.CountryCode, _sadc.AllowRandInCommonMonetaryArea);
                throw new RequestValidationException(
                    "currencyCode",
                    $"Currency {currency} is not permitted for customers in {customer.CountryCode}. Allowed: {string.Join(", ", allowed)}.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var order = Order.Create(
                customer.Id,
                currency,
                request.LineItems!.Select(li => new NewOrderLine(li.ProductSku!, li.Quantity, li.UnitPrice)).ToList(),
                now);

            // Transactional outbox: the repository saves the order and its event in one transaction.
            // A broker outage can delay the event but never lose it.
            var correlationId = _correlation.CorrelationId;
            var messageId = Guid.NewGuid();
            var @event = new OrderCreated(messageId, order.Id, order.CustomerId, order.CurrencyCode, order.TotalAmount, order.CreatedAt, correlationId);
            await _orders.AddWithOutboxAsync(
                order,
                new OutboxMessage
                {
                    Id = messageId,
                    Type = OrderCreated.MessageType,
                    Payload = JsonSerializer.Serialize(@event, ResponseJson),
                    CorrelationId = correlationId,
                    OccurredAt = now,
                },
                ct);

            _logger.LogInformation(
                "Order {OrderId} created for customer {CustomerId}: {Total} {Currency}, {LineCount} lines",
                order.Id, order.CustomerId, order.TotalAmount, order.CurrencyCode, order.LineItems.Count);

            return new OrderWithETag(ToDto(order, customer.Name), ETags.From(order.RowVersion));
        }

        public async Task<OrderWithETag> GetAsync(Guid id, CancellationToken ct)
        {
            var row = await _orders.GetAsync(id, ct) ?? throw new NotFoundException("Order", id);

            var dto = new OrderDto(
                row.Id,
                row.CustomerId,
                row.CustomerName,
                row.Status.ToString(),
                row.CreatedAt,
                row.UpdatedAt,
                row.CurrencyCode,
                row.TotalAmount,
                row.LineItems,
                AllowedTransitions(row.Status));

            return new OrderWithETag(dto, ETags.From(row.RowVersion));
        }

        public Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListQuery query, CancellationToken ct)
        {
            OrderStatus? status = StatusParser.TryParser(query.Status, out var parsed) ? parsed : null;
            return _orders.ListAsync(
                query.CustomerId,
                status,
                query.Sort ?? OrderSort.Default,
                query.Page ?? 1,
                query.PageSize ?? Paging.DefaultPageSize,
                ct);
        }

        public async Task<UpdateOrderStatusResult> UpdateStatusAsync(UpdateOrderStatusCommand command, CancellationToken ct)
        {
            if (!StatusParser.TryParser(command.Status, out var target))
            {
                throw new RequestValidationException("status", "Unknown status.");
            }

            // Scope the key to this operation and order, so one client key can't collide across resources.
            var recordKey = $"order-status:{command.OrderId:N}:{command.IdempotencyKey}";
            var requestHash = Hash(target.ToString());

            var existing = await _idempotency.FindAsync(recordKey, ct);
            if (existing is not null)
            {
                return Replay(existing, requestHash);
            }

            try
            {
                return await ApplyStatusChangeAsync(command, target, recordKey, requestHash, ct);
            }
            catch (Exception ex) when (ex is InvalidStatusTransitionException or ConcurrencyConflictException or WriteConflictException)
            {
                // A concurrent request with the same key may have just committed, so our read of the
                // order went stale (rowversion mismatch or "Paid -> Paid"), or our insert hit the
                // record's primary key. Either way the right answer for a retry is the winner's response.
                var winner = await _idempotency.FindAsync(recordKey, ct);
                if (winner is not null)
                {
                    return Replay(winner, requestHash);
                }

                if (ex is ConcurrencyConflictException)
                {
                    // Someone else (another user, or the worker) changed the order after we read it.
                    // Return 409 with the current state so the client can decide what to do.
                    var current = await GetAsync(command.OrderId, ct);
                    throw new ConflictException("concurrency_conflict", "The order was updated by another process. Review its current state and retry.", current: current.Order);
                }

                throw;
            }
        }

        private async Task<UpdateOrderStatusResult> ApplyStatusChangeAsync(
            UpdateOrderStatusCommand command,
            OrderStatus target,
            string recordKey,
            string requestHash,
            CancellationToken ct)
        {
            var order = await _orders.GetForUpdateAsync(command.OrderId, ct)
                ?? throw new NotFoundException("Order", command.OrderId);

            if (command.IfMatch is not null && command.IfMatch != ETags.From(order.RowVersion))
            {
                throw new PreconditionFailedException("The order has changed since you last read it. Reload it and try again.");
            }

            if (target == OrderStatus.Cancelled && order.Status == OrderStatus.Paid && !command.CallerIsAdmin)
            {
                throw new ForbiddenException("Cancelling a paid order requires the Orders.Admin role.");
            }

            var previous = order.Status;
            order.TransitionTo(target, _time.GetUtcNow().UtcDateTime); // throws InvalidStatusTransitionException -> 409

            var body = JsonSerializer.Serialize(ToDto(order, order.Customer!.Name), ResponseJson);

            // The status change and the idempotency record are saved atomically: either both or neither.
            await _orders.SaveStatusChangeAsync(
                order,
                new IdempotencyRecord
                {
                    Key = recordKey,
                    RequestHash = requestHash,
                    ResponseStatusCode = 200,
                    ResponseBody = body,
                    CreatedAt = _time.GetUtcNow().UtcDateTime,
                },
                ct);

            _logger.LogInformation("Order {OrderId} moved {From} -> {To}", order.Id, previous, target);
            return new UpdateOrderStatusResult(200, body, ETags.From(order.RowVersion), Replayed: false);
        }

        internal static IReadOnlyList<string> AllowedTransitions(OrderStatus status) =>
            OrderStatusTransitions.NextStatuses(status).Select(s => s.ToString()).ToList();

        private static OrderDto ToDto(Order order, string customerName) => new(
            order.Id,
            order.CustomerId,
            customerName,
            order.Status.ToString(),
            order.CreatedAt,
            order.UpdatedAt,
            order.CurrencyCode,
            order.TotalAmount,
            order.LineItems
                .OrderBy(li => li.ProductSku, StringComparer.Ordinal)
                .Select(li => new OrderLineItemDto(li.Id, li.ProductSku, li.Quantity, li.UnitPrice, li.LineTotal))
                .ToList(),
            AllowedTransitions(order.Status));

        private static UpdateOrderStatusResult Replay(IdempotencyRecord record, string requestHash)
        {
            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new IdempotencyKeyReuseException();
            }

            return new UpdateOrderStatusResult(record.ResponseStatusCode, record.ResponseBody, ETag: null, Replayed: true);
        }

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static class ETags
    {
        public static string From(byte[] rowVersion) => $"\"{Convert.ToHexString(rowVersion)}\"";
    }
}
