using System.Net;
using System.Net.Http.Json;
using CityPortal.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CityPortal.IntegrationTests;

public sealed class WeatherIntegrationTests : IClassFixture<RegistrationFixture>
{
    private readonly RegistrationFixture _fixture;

    public WeatherIntegrationTests(RegistrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task WeatherRefresh_PreservesLastReadingOnFailure_ExpiresAndRecoversAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, _) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage disabled = await client.GetAsync("/api/weather/current");
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);

        await SetWeatherLocationAsync(factory, 56.8, 60.6);
        int code = 73;
        HttpStatusCode status = HttpStatusCode.OK;
        using HttpClient provider = Provider((request, _) =>
        {
            Assert.Equal("https://api.open-meteo.com/v1/forecast?latitude=56.8&longitude=60.6&current=temperature_2m,weather_code&temperature_unit=celsius&timeformat=unixtime",
                request.RequestUri!.AbsoluteUri);
            return Task.FromResult(ProviderReading(time, code, status));
        });
        await RefreshAsync(factory, time, provider);
        using HttpResponseMessage response = await client.GetAsync("/api/weather/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        CurrentWeatherResponse fresh = Assert.IsType<CurrentWeatherResponse>(await response.Content.ReadFromJsonAsync<CurrentWeatherResponse>());
        Assert.Equal(new CurrentWeatherResponse(-5.25, 73, DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds()), false), fresh);

        status = HttpStatusCode.ServiceUnavailable;
        time.Advance(TimeSpan.FromHours(1));
        await RefreshAsync(factory, time, provider);
        Assert.Equal(fresh with { IsStale = true }, await client.GetFromJsonAsync<CurrentWeatherResponse>("/api/weather/current"));

        time.Advance(TimeSpan.FromHours(2));
        using HttpResponseMessage expired = await client.GetAsync("/api/weather/current");
        Assert.Equal(HttpStatusCode.NoContent, expired.StatusCode);
        status = HttpStatusCode.OK;
        code = 0;
        await RefreshAsync(factory, time, provider);
        CurrentWeatherResponse recovered = Assert.IsType<CurrentWeatherResponse>(await client.GetFromJsonAsync<CurrentWeatherResponse>("/api/weather/current"));
        Assert.Equal(0, recovered.WeatherCode);
        Assert.False(recovered.IsStale);
        Assert.True(recovered.ObservedAtUtc > fresh.ObservedAtUtc);
    }

    [Fact]
    public async Task WeatherRefresh_DiscardsReadingWhenLocationChangesDuringRequestAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, _) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        using HttpClient client = factory.CreateClient();
        await SetWeatherLocationAsync(factory, 56.8, 60.6);
        using HttpClient initial = Provider((_, _) => Task.FromResult(ProviderReading(time, 73)));
        await RefreshAsync(factory, time, initial);

        using HttpClient provider = Provider(async (_, _) =>
        {
            await SetWeatherLocationAsync(factory, 55.75, 37.62);
            return ProviderReading(time, 0);
        });
        await RefreshAsync(factory, time, provider);
        using HttpResponseMessage noWeather = await client.GetAsync("/api/weather/current");
        Assert.Equal(HttpStatusCode.NoContent, noWeather.StatusCode);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        WeatherSnapshot snapshot = await scope.ServiceProvider.GetRequiredService<IntegrationDbContext>().WeatherSnapshots.SingleAsync();
        Assert.Equal(56.8, snapshot.Latitude);
        Assert.Equal(73, snapshot.WeatherCode);
    }

    private static async Task SetWeatherLocationAsync(ApiFactory factory, double latitude, double longitude)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IntegrationDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        IntegrationSettings settings = await context.Settings.SingleAsync();
        settings.WeatherEnabled = true;
        settings.Latitude = latitude;
        settings.Longitude = longitude;
        settings.Version++;
        await context.SaveChangesAsync();
    }

    private static async Task RefreshAsync(ApiFactory factory, TimeProvider time, HttpClient provider)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var job = new WeatherRefreshJob(scope.ServiceProvider.GetRequiredService<IntegrationDbContext>(),
            new OpenMeteoClient(provider), time, NullLogger<WeatherRefreshJob>.Instance);
        await job.RefreshAsync(CancellationToken.None);
    }

    private static HttpClient Provider(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new WeatherHandler(send)) { BaseAddress = new Uri("https://api.open-meteo.com/") };

    private static HttpResponseMessage ProviderReading(TimeProvider time, int code, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = JsonContent.Create(new { current = new { time = time.GetUtcNow().ToUnixTimeSeconds(), temperature_2m = -5.25, weather_code = code } })
        };

    private sealed class WeatherHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public WeatherHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _send(request, cancellationToken);
    }
}
