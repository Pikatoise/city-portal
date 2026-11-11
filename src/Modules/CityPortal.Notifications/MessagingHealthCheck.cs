using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace CityPortal.Notifications;

internal sealed class MessagingHealthCheck : IHealthCheck
{
    private readonly ConnectionFactory _connectionFactory;

    public MessagingHealthCheck(ConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // A fresh probe also checks credentials and broker availability after an outage.
        await using IConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return connection.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }
}
