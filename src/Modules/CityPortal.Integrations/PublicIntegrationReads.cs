using Microsoft.EntityFrameworkCore;

namespace CityPortal.Integrations;

public sealed class PublicIntegrationReads
{
    private readonly IntegrationDbContext _context;
    private readonly TimeProvider _time;

    public PublicIntegrationReads(IntegrationDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public Task<bool> IsRssEnabledAsync(CancellationToken cancellationToken) =>
        _context.Settings.AnyAsync(item => item.RssEnabled, cancellationToken);

    public async Task<CurrentWeatherResponse?> CurrentWeatherAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset expires = now.AddHours(-3);
        DateTimeOffset stale = now.AddHours(-1);
        return await (from settings in _context.Settings
                      from snapshot in _context.WeatherSnapshots
                      where settings.WeatherEnabled && settings.Latitude == snapshot.Latitude && settings.Longitude == snapshot.Longitude
                          && snapshot.RefreshedAtUtc > expires && snapshot.ObservedAtUtc > expires
                      select new CurrentWeatherResponse(snapshot.Celsius, snapshot.WeatherCode, snapshot.ObservedAtUtc,
                          snapshot.RefreshedAtUtc <= stale || snapshot.ObservedAtUtc <= stale))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
