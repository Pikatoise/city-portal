using CityPortal.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CityPortal.Api;

internal sealed class InstallationHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;

    public InstallationHealthCheck(IServiceScopeFactory scopes, IConfiguration configuration)
    {
        _scopes = scopes;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_configuration.GetValue<bool>("Installation:Enabled"))
            return HealthCheckResult.Healthy();
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        bool installed = (await scope.ServiceProvider.GetRequiredService<SiteInstallation>().GetAsync(cancellationToken)).Installed;
        return installed ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Complete installation locally before enabling public access.");
    }
}
