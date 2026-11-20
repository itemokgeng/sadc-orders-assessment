using Core.DTOs;
using Core.Interface;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataLayer.Messaging;

/// <summary>
/// Publishes one batch of pending outbox rows. Kept separate from the hosted service so integration
/// tests can call it directly.
/// </summary>
public class OutboxProcessor
{
    public const int BatchSize = 50;
    public const int MaxAttempts = 20;

    private readonly OrdersDbContext _db;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(OrdersDbContext db, IMessagePublisher publisher, ILogger<OutboxProcessor> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    /// <returns>How many messages were published.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        // Tracked query: we update these rows. Uses the filtered index on OccurredAt.
        var pending = await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in pending)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = message.CorrelationId,
                ["MessageId"] = message.Id,
            });

            try
            {
                await _publisher.PublishAsync(
                    new OutgoingMessage(message.Id, message.Type, RoutingKeyFor(message.Type), message.Payload, message.CorrelationId),
                    ct);
                message.ProcessedAt = DateTime.UtcNow;
                message.LastError = null;
                published++;
                _logger.LogInformation("Published {MessageType} {MessageId}", message.Type, message.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = Truncate(ex.Message, 2000);
                _logger.LogWarning(ex, "Publishing {MessageType} {MessageId} failed (attempt {Attempt})", message.Type, message.Id, message.Attempts);

                // Broker probably down: stop here and leave the rest for the next poll.
                await _db.SaveChangesAsync(ct);
                return published;
            }

            // Save after each message so a crash re-sends at most one already-published message.
            // The consumer is idempotent, so a duplicate does no harm.
            await _db.SaveChangesAsync(ct);
        }

        return published;
    }

    private static string RoutingKeyFor(string type) => type switch
    {
        OrderCreated.MessageType => RabbitMqTopology.OrderCreatedRoutingKey,
        _ => throw new InvalidOperationException($"No routing key for message type '{type}'."),
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
