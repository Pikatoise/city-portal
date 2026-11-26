using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Quartz;

namespace CityPortal.Integrations;

[DisallowConcurrentExecution]
public sealed class WeatherRefreshJob : IJob
{
    private static readonly Meter _meter = new("CityPortal.Weather");
    private static readonly Counter<long> _refreshes = _meter.CreateCounter<long>("cityportal.weather.refreshes");

    private readonly IntegrationDbContext _context;
    private readonly OpenMeteoClient _client;
    private readonly TimeProvider _time;
    private readonly ILogger<WeatherRefreshJob> _logger;

    public WeatherRefreshJob(IntegrationDbContext context, OpenMeteoClient client, TimeProvider time, ILogger<WeatherRefreshJob> logger)
    {
        _context = context;
        _client = client;
        _time = time;
        _logger = logger;
    }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) => new(RefreshAsync(cancellationToken));

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IntegrationSettings settings = await _context.Settings.AsNoTracking().SingleAsync(cancellationToken);
        if (!settings.WeatherEnabled || settings.Latitude is not double latitude || settings.Longitude is not double longitude)
            return;

        WeatherSnapshot? observation;
        try
        {
            observation = await _client.ReadAsync(latitude, longitude, _time.GetUtcNow(), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is HttpRequestException or JsonException or OperationCanceledException or NotSupportedException)
        {
            ReportFailure();
            return;
        }
        if (observation is null)
        {
            ReportFailure();
            return;
        }
        observation.RefreshedAtUtc = _time.GetUtcNow();

        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        IntegrationSettings current = await _context.Settings.FromSqlRaw("""
            SELECT * FROM integrations.settings WHERE "Id" = 1 FOR UPDATE
            """).AsNoTracking().SingleAsync(cancellationToken);
        // An administration change during the HTTP request may invalidate the observation.
        if (current.Version != settings.Version)
            return;
        WeatherSnapshot? previous = await _context.WeatherSnapshots.SingleOrDefaultAsync(cancellationToken);
        if (previous is null)
            _context.WeatherSnapshots.Add(observation);
        else
            _context.Entry(previous).CurrentValues.SetValues(observation);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _refreshes.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
    }

    private void ReportFailure()
    {
        _refreshes.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
        _logger.LogWarning("The weather provider did not supply a valid current observation.");
    }
}
