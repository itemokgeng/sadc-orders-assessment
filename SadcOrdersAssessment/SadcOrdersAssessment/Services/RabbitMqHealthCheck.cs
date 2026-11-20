using DataLayer.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SadcOrdersAssessment.Services
{
    public sealed class RabbitMqHealthCheck : IHealthCheck
    {
        private readonly RabbitMqConnection _connection;

        public RabbitMqHealthCheck(RabbitMqConnection connection)
        {
            _connection = connection;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return Task.FromResult(_connection.GetConnection().IsOpen
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("RabbitMQ connection is closed."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", ex));
            }
        }
    }
}