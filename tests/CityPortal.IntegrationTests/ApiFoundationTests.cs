using System.Net;
using System.Text.Json;
using CityPortal.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CityPortal.IntegrationTests;

public sealed class ApiFoundationTests : IClassFixture<RegistrationFixture>
{
    private readonly RegistrationFixture _fixture;

    public ApiFoundationTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }
    private const string _unavailableDatabase = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1";
    private const string _unavailableMessaging = "amqp://test:test@127.0.0.1:1";

    [Theory]
    [InlineData("Asia/Yekaterinburg", "Asia/Yekaterinburg")]
    [InlineData("Russian Standard Time", "Europe/Moscow")]
    public async Task SystemInfo_WithConfiguredCityTimeZone_ReturnsBrowserCompatibleIdentifierAsync(string configuredId, string expectedId)
    {
        (ApiFactory factory, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        await using WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder => builder.UseSetting("City:TimeZone", configuredId));
        using HttpClient client = configured.CreateClient();
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/system/info"));
        Assert.Equal(expectedId, body.RootElement.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task SystemInfo_ReturnsOnlyPublicApplicationMetadataAsync()
    {
        (ApiFactory factory, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/system/info", CancellationToken.None);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CityPortal", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("stage").GetInt32());
        Assert.Equal("UTC", body.RootElement.GetProperty("timeZone").GetString());
        Assert.Equal("", body.RootElement.GetProperty("city").GetString());
        Assert.Equal("", body.RootElement.GetProperty("region").GetString());
        Assert.Equal(5, body.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task Liveness_WhenDependenciesAreUnavailable_RemainsHealthyAsync()
    {
        await using var factory = new ApiFactory("Production", _unavailableDatabase, _unavailableMessaging);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/live", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Readiness_WhenDependenciesAreUnavailable_ReturnsServiceUnavailableAsync()
    {
        await using var factory = new ApiFactory("Production", _unavailableDatabase, _unavailableMessaging);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/ready", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetailsAsync()
    {
        await using var factory = new ApiFactory("Production", _unavailableDatabase, _unavailableMessaging);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/missing", CancellationToken.None);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task OpenApi_InDevelopment_DescribesSystemInfoContractAsync()
    {
        await using var factory = new ApiFactory("Development", _unavailableDatabase, _unavailableMessaging);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement operation = body.RootElement.GetProperty("paths").GetProperty("/api/system/info").GetProperty("get");
        Assert.Equal("getSystemInfo", operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task ApiDocumentation_InProduction_IsUnavailableAsync(string path)
    {
        await using var factory = new ApiFactory("Production", _unavailableDatabase, _unavailableMessaging);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
