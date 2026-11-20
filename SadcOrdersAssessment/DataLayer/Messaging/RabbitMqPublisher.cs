using System.Text;
using Core.DTOs;
using Core.Interface;
using RabbitMQ.Client;

namespace DataLayer.Messaging;

/// <summary>
/// Publishes persistent messages with publisher confirms. The outbox only marks a row as sent once
/// the broker has confirmed it, which together with the outbox gives at-least-once delivery.
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(5);
    private readonly RabbitMqConnection _connection;

    public RabbitMqPublisher(RabbitMqConnection connection)
    {
        _connection = connection;
    }

    public Task PublishAsync(OutgoingMessage message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // IModel isn't thread-safe. A channel per publish is simple and fine at this volume.
        using var channel = _connection.GetConnection().CreateModel();
        channel.ConfirmSelect();

        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        props.MessageId = message.MessageId.ToString();
        props.Type = message.Type;
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        props.CorrelationId = message.CorrelationId;
        props.Headers = new Dictionary<string, object>();
        if (message.CorrelationId is not null)
        {
            props.Headers[RabbitMqTopology.CorrelationIdHeader] = message.CorrelationId;
        }

        channel.BasicPublish(
            exchange: RabbitMqTopology.Exchange,
            routingKey: message.RoutingKey,
            mandatory: false,
            basicProperties: props,
            body: Encoding.UTF8.GetBytes(message.Body));

        channel.WaitForConfirmsOrDie(ConfirmTimeout);
        return Task.CompletedTask;
    }
}
