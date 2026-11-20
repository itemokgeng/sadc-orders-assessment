namespace DataLayer.Messaging;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>How many unacknowledged messages a consumer may hold at once (back-pressure).</summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Wait before each retry. Each entry becomes its own TTL queue, so a long delay never holds up
    /// a short one. A message that still fails after the last delay goes to the dead-letter queue.
    /// </summary>
    public int[] RetryDelaysSeconds { get; set; } = { 5, 30, 120 };
}
