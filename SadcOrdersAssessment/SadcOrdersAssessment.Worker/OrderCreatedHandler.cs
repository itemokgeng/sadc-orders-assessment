using Core.DTOs;
using DataLayer.Context;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SadcOrders.Worker;

public enum HandleOutcome
{
    Fulfilled,
    DuplicateSkipped,
    AlreadyFinal,
}

/// <summary>Thrown when <see cref="WorkerOptions.SimulatedFailureRate"/> triggers. Used to demo retries.</summary>
public sealed class SimulatedProcessingException : Exception
{
    public SimulatedProcessingException() : base("Simulated downstream failure.")
    {
    }
}

/// <summary>
/// Simulates downstream processing (payment capture, then stock allocation) and moves the order
/// Pending -> Paid -> Fulfilled. It is idempotent in two ways:
/// 1. Dedup: MessageId is written to ProcessedMessages in the same SaveChanges as the status change,
///    so a redelivered message is skipped.
/// 2. State check: orders already Fulfilled or Cancelled (e.g. a user cancelled meanwhile) are left alone.
/// Concurrent edits are caught by Orders.RowVersion. The resulting exception goes back to the
/// consumer, which retries, and the retry then reads the fresh state.
/// </summary>
public sealed class OrderCreatedHandler
{
    private readonly OrdersDbContext _db;
    private readonly TimeProvider _time;
    private readonly WorkerOptions _options;
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(OrdersDbContext db, TimeProvider time, IOptions<WorkerOptions> options, ILogger<OrderCreatedHandler> logger)
    {
        _db = db;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HandleOutcome> HandleAsync(OrderCreated message, CancellationToken ct)
    {
        if (await _db.ProcessedMessages.AsNoTracking().AnyAsync(m => m.MessageId == message.MessageId, ct))
        {
            _logger.LogInformation("Duplicate message {MessageId} skipped", message.MessageId);
            return HandleOutcome.DuplicateSkipped;
        }

        // Missing order: treat as transient (e.g. reading a lagging replica) and let it retry,
        // then go to the DLQ.
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId, ct)
            ?? throw new InvalidOperationException($"Order {message.OrderId} not found.");

        var outcome = HandleOutcome.AlreadyFinal;
        if (order.Status is OrderStatus.Pending or OrderStatus.Paid)
        {
            await SimulateDownstreamAsync(ct);

            var now = _time.GetUtcNow().UtcDateTime;
            if (order.Status == OrderStatus.Pending)
            {
                order.TransitionTo(OrderStatus.Paid, now);
            }

            order.TransitionTo(OrderStatus.Fulfilled, now);
            outcome = HandleOutcome.Fulfilled;
        }
        else
        {
            _logger.LogInformation("Order {OrderId} is already {Status}; nothing to do", order.Id, order.Status);
        }

        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = message.MessageId,
            MessageType = OrderCreated.MessageType,
            ProcessedAt = _time.GetUtcNow().UtcDateTime,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // PK clash on ProcessedMessages: another consumer finished this message first.
            _db.ChangeTracker.Clear();
            if (await _db.ProcessedMessages.AsNoTracking().AnyAsync(m => m.MessageId == message.MessageId, ct))
            {
                _logger.LogInformation("Message {MessageId} was processed concurrently; skipping", message.MessageId);
                return HandleOutcome.DuplicateSkipped;
            }

            throw;
        }

        if (outcome == HandleOutcome.Fulfilled)
        {
            _logger.LogInformation("Order {OrderId} fulfilled", order.Id);
        }

        return outcome;
    }

    private async Task SimulateDownstreamAsync(CancellationToken ct)
    {
        if (_options.SimulatedProcessingDelayMs > 0)
        {
            await Task.Delay(_options.SimulatedProcessingDelayMs, ct);
        }

        if (_options.SimulatedFailureRate > 0 && Random.Shared.NextDouble() < _options.SimulatedFailureRate)
        {
            throw new SimulatedProcessingException();
        }
    }
}
