using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CityPortal.Media;

internal sealed class MediaStorageHealthCheck : IHealthCheck
{
    private readonly MediaObjectStore _storage;

    public MediaStorageHealthCheck(MediaObjectStore storage)
    {
        _storage = storage;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _storage.AvailableAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Media storage is unavailable.");
        }
    }
}
