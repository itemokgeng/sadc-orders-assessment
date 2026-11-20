using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DataLayer.Messaging;

/// <summary>
/// One long-lived connection per process, as RabbitMQ recommends. Channels are cheap and are created
/// per use. The connection is opened lazily, so the API can start (and report not-ready) while
/// RabbitMQ is still booting. Automatic recovery reconnects after broker restarts.
/// </summary>
public sealed class RabbitMqConnection : IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly object _sync = new();
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public RabbitMqOptions Options => _options;

    public bool IsConnected => _connection is { IsOpen: true };

    /// <summary>Returns the open connection, connecting first if needed. Throws if the broker can't be reached.</summary>
    public IConnection GetConnection()
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        lock (_sync)
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection?.Dispose();

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                ClientProvidedName = AppDomain.CurrentDomain.FriendlyName,
            };

            _connection = factory.CreateConnection();
            _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", _options.HostName, _options.Port);

            using var channel = _connection.CreateModel();
            RabbitMqTopology.Declare(channel, _options);
            return _connection;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
