using RabbitMQ.Client;

namespace DataLayer.Messaging
{
    public static class RabbitMqTopology
    {
        public const string Exchange = "sadc.orders";
        public const string RetryExchange = "sadc.orders.retry";
        public const string DeadLetterExchange = "sadc.orders.dlx";

        public const string OrderCreatedRoutingKey = "order.created";
        public const string OrderCreatedQueue = "orders.created";
        public const string OrderCreatedDeadLetterQueue = "orders.created.dlq";
        public const string OrderCreatedDeadRoutingKey = "order.created.dead";

        public const string RetryCountHeader = "x-retry-count";
        public const string CorrelationIdHeader = "x-correlation-id";

        public static string RetryQueueName(int attempt) => $"{OrderCreatedQueue}.retry.{attempt}";

        public static string RetryRoutingKey(int attempt) => $"retry.{attempt}";

        public static void Declare(IModel channel, RabbitMqOptions options)
        {
            channel.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
            channel.ExchangeDeclare(RetryExchange, ExchangeType.Direct, durable: true, autoDelete: false);
            channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);

            // Main work queue. A nack with requeue:false (poison message, or retries used up) sends the
            // message to the DLX, and the broker records why in the x-death header.
            channel.QueueDeclare(
                OrderCreatedQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    ["x-dead-letter-exchange"] = DeadLetterExchange,
                    ["x-dead-letter-routing-key"] = OrderCreatedDeadRoutingKey,
                });
            channel.QueueBind(OrderCreatedQueue, Exchange, OrderCreatedRoutingKey);

            // One delay queue per retry attempt (exponential-ish backoff). Nothing consumes these queues:
            // when the TTL expires, the message is dead-lettered back to the main exchange.
            for (var attempt = 1; attempt <= options.RetryDelaysSeconds.Length; attempt++)
            {
                var queue = RetryQueueName(attempt);
                channel.QueueDeclare(
                    queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object>
                    {
                        ["x-message-ttl"] = options.RetryDelaysSeconds[attempt - 1] * 1000,
                        ["x-dead-letter-exchange"] = Exchange,
                        ["x-dead-letter-routing-key"] = OrderCreatedRoutingKey,
                    });
                channel.QueueBind(queue, RetryExchange, RetryRoutingKey(attempt));
            }

            channel.QueueDeclare(OrderCreatedDeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.QueueBind(OrderCreatedDeadLetterQueue, DeadLetterExchange, OrderCreatedDeadRoutingKey);
        }
    }
}