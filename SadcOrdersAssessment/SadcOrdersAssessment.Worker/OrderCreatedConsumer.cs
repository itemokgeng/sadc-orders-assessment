using System.Text;
using System.Text.Json;
using Core.DTOs;
using DataLayer.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SadcOrders.Worker;

/// <summary>
/// Consumes orders.created with manual acks (at-least-once delivery).
/// - Success: ack.
/// - Handler failure: republish to the next retry queue (delays 5s, 30s, 120s by default) with
///   x-retry-count incremented, wait for the publisher confirm, then ack the original.
/// - Retries used up, or a poison message (unreadable JSON): nack without requeue, so the broker
///   dead-letters it to orders.created.dlq and records the reason in x-death.
/// Prefetch caps unacked messages per consumer. That is the back-pressure that stops a burst of
/// orders from overwhelming the database.
/// </summary>
public sealed class OrderCreatedConsumer : BackgroundService
{
    private readonly RabbitMqConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderCreatedConsumer> _logger;
    private IModel? _channel;
    private CancellationToken _stoppingToken;

    public OrderCreatedConsumer(RabbitMqConnection connection, IServiceScopeFactory scopeFactory, ILogger<OrderCreatedConsumer> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        while (!stoppingToken.IsCancellationRequested && _channel is null)
        {
            try
            {
                var channel = _connection.GetConnection().CreateModel();
                channel.BasicQos(prefetchSize: 0, prefetchCount: _connection.Options.PrefetchCount, global: false);
                channel.ConfirmSelect();

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += OnReceivedAsync;
                channel.BasicConsume(RabbitMqTopology.OrderCreatedQueue, autoAck: false, consumer: consumer);
                _channel = channel;
                _logger.LogInformation("Consuming {Queue} (prefetch {Prefetch})", RabbitMqTopology.OrderCreatedQueue, _connection.Options.PrefetchCount);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "RabbitMQ not reachable yet; retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs delivery)
    {
        var channel = _channel!;
        var props = delivery.BasicProperties;
        var correlationId = props.CorrelationId ?? ReadHeader(props, RabbitMqTopology.CorrelationIdHeader);
        var retryCount = ReadRetryCount(props);

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["MessageId"] = props.MessageId,
            ["RetryCount"] = retryCount,
        });

        OrderCreated? message;
        try
        {
            message = MessageSerializer.Deserialize<OrderCreated>(delivery.Body.Span);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Poison message (invalid JSON); dead-lettering");
            channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (message is null || message.OrderId == Guid.Empty || message.MessageId == Guid.Empty)
        {
            _logger.LogError("Poison message (missing fields); dead-lettering");
            channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        using var orderScope = _logger.BeginScope(new Dictionary<string, object?> { ["OrderId"] = message.OrderId });
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<OrderCreatedHandler>();
            var outcome = await handler.HandleAsync(message, _stoppingToken);
            channel.BasicAck(delivery.DeliveryTag, multiple: false);
            _logger.LogInformation("Message handled: {Outcome}", outcome);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Shutting down: return the message to the queue so the next instance processes it.
            channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            var delays = _connection.Options.RetryDelaysSeconds;
            if (retryCount < delays.Length)
            {
                var nextAttempt = retryCount + 1;
                _logger.LogWarning(ex, "Processing failed; retry {Attempt}/{Max} in {Delay}s", nextAttempt, delays.Length, delays[nextAttempt - 1]);
                ScheduleRetry(channel, delivery, nextAttempt);
                channel.BasicAck(delivery.DeliveryTag, multiple: false);
            }
            else
            {
                _logger.LogError(ex, "Processing failed after {Retries} retries; dead-lettering", retryCount);
                channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: false);
            }
        }
    }

    private static void ScheduleRetry(IModel channel, BasicDeliverEventArgs delivery, int attempt)
    {
        var original = delivery.BasicProperties;
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = original.ContentType;
        props.MessageId = original.MessageId;
        props.CorrelationId = original.CorrelationId;
        props.Type = original.Type;
        props.Headers = original.Headers is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(original.Headers);
        props.Headers[RabbitMqTopology.RetryCountHeader] = attempt;

        channel.BasicPublish(RabbitMqTopology.RetryExchange, RabbitMqTopology.RetryRoutingKey(attempt), mandatory: false, basicProperties: props, body: delivery.Body);

        // Only ack the original once the broker has confirmed the retry copy, so nothing is lost
        // if the process dies in between. The worst case is a duplicate, which the handler skips.
        channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
    }

    private static int ReadRetryCount(IBasicProperties props)
    {
        if (props.Headers is null || !props.Headers.TryGetValue(RabbitMqTopology.RetryCountHeader, out var value))
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0,
        };
    }

    private static string? ReadHeader(IBasicProperties props, string name) =>
        props.Headers is not null && props.Headers.TryGetValue(name, out var value) && value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}
