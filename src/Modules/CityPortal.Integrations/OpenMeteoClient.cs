using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CityPortal.Integrations;

public sealed class OpenMeteoClient
{
    private readonly HttpClient _http;

    public OpenMeteoClient(HttpClient http) => _http = http;

    internal async Task<WeatherSnapshot?> ReadAsync(double latitude, double longitude, DateTimeOffset requestedAt, CancellationToken cancellationToken)
    {
        string resource = string.Create(CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&temperature_unit=celsius&timeformat=unixtime");
        OpenMeteoResponse? response = await _http.GetFromJsonAsync<OpenMeteoResponse>(resource, cancellationToken);
        if (response?.Current is not { Time: long time, Celsius: double celsius, WeatherCode: int code }
            || !double.IsFinite(celsius) || celsius is < -100 or > 70 || code is < 0 or > 99
            || time < requestedAt.AddHours(-3).ToUnixTimeSeconds() || time > requestedAt.AddMinutes(5).ToUnixTimeSeconds())
            return null;
        return new WeatherSnapshot
        {
            Latitude = latitude,
            Longitude = longitude,
            Celsius = celsius,
            WeatherCode = code,
            ObservedAtUtc = DateTimeOffset.FromUnixTimeSeconds(time)
        };
    }

    private sealed record OpenMeteoResponse(OpenMeteoCurrent? Current);

    private sealed record OpenMeteoCurrent(long? Time, [property: JsonPropertyName("temperature_2m")] double? Celsius,
        [property: JsonPropertyName("weather_code")] int? WeatherCode);
}
