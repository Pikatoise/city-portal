namespace CityPortal.Integrations;

public sealed class WeatherSnapshot
{
    public int Id { get; set; } = 1;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double Celsius { get; set; }

    public int WeatherCode { get; set; }

    public DateTimeOffset ObservedAtUtc { get; set; }

    public DateTimeOffset RefreshedAtUtc { get; set; }
}
