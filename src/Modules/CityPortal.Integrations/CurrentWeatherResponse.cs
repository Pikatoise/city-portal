namespace CityPortal.Integrations;

/// <summary>Current weather for the configured city and whether its observation is stale.</summary>
public sealed record CurrentWeatherResponse(double Celsius, int WeatherCode, DateTimeOffset ObservedAtUtc, bool IsStale);
