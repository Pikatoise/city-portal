using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.CitizenSubmissions;
using CityPortal.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class CitizenSuggestionTests : IClassFixture<RegistrationFixture>
{
    private const string _path = "/api/account/suggestions";
    private readonly RegistrationFixture _fixture;

    public CitizenSuggestionTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ResidentSuggestion_EnforcesConfirmationOwnershipVersionsAndExpiryAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        await factory.Services.MigrateCitizenSubmissionsAsync(CancellationToken.None);
        using HttpClient resident = await LoginAsync(factory, database, "resident", confirmed: true);
        using HttpClient other = await LoginAsync(factory, database, "neighbour", confirmed: true);
        using HttpClient unconfirmed = await LoginAsync(factory, database, "unconfirmed", confirmed: false);
        using HttpClient anonymous = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var input = new SuggestionInput { Title = " River cleanup ", Description = "Volunteers are needed.", Contact = "resident@example.test", EventDate = new DateOnly(2026, 11, 20) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(_path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unconfirmed.PostAsJsonAsync(_path, input)).StatusCode);
        resident.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await resident.PostAsJsonAsync(_path, input)).StatusCode);
        await CsrfAsync(resident);
        Assert.Equal(HttpStatusCode.BadRequest, (await resident.PostAsJsonAsync(_path, input with { Title = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await resident.PostAsJsonAsync(_path, input with { Location = "\0" })).StatusCode);

        using HttpResponseMessage created = await resident.PostAsJsonAsync(_path, input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        SuggestionResponse suggestion = (await created.Content.ReadFromJsonAsync<SuggestionResponse>())!;
        string itemPath = $"{_path}/{suggestion.Id}";
        Assert.Equal(itemPath, created.Headers.Location?.AbsolutePath);
        Assert.Equal("River cleanup", suggestion.Title);
        Assert.Equal(input.Contact, suggestion.Contact);
        Assert.Equal(input.EventDate, suggestion.EventDate);
        Assert.Equal(TimeSpan.FromHours(72), suggestion.ExpiresAtUtc - suggestion.CreatedAtUtc);
        Assert.True(suggestion.CanEdit);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(itemPath)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<SuggestionPage>(_path))!.Items);

        var change = new UpdateSuggestionRequest { Details = input with { Title = "Updated title" }, Version = suggestion.Version };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync(itemPath, change)).StatusCode);
        HttpResponseMessage[] attempts = await Task.WhenAll(resident.PutAsJsonAsync(itemPath, change),
            resident.PutAsJsonAsync(itemPath, change with { Details = input with { Title = "Competing title" } }));
        using HttpResponseMessage winner = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        using HttpResponseMessage loser = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        SuggestionResponse saved = (await winner.Content.ReadFromJsonAsync<SuggestionResponse>())!;
        Assert.Equal(suggestion.Version + 1, saved.Version);
        Assert.Equal(saved.Title, Assert.Single((await resident.GetFromJsonAsync<SuggestionPage>(_path))!.Items).Title);

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var claim = new NpgsqlCommand("UPDATE citizen_submissions.\"Suggestions\" SET \"FirstClaimedAtUtc\" = \"CreatedAtUtc\" WHERE \"Id\" = @id", connection);
        claim.Parameters.AddWithValue("id", suggestion.Id);
        Assert.Equal(1, await claim.ExecuteNonQueryAsync());
        Assert.False((await resident.GetFromJsonAsync<SuggestionResponse>(itemPath))!.CanEdit);
        Assert.Equal(HttpStatusCode.Conflict, (await resident.PutAsJsonAsync(itemPath, change with { Version = saved.Version })).StatusCode);

        time.Advance(TimeSpan.FromHours(72));
        Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync(itemPath)).StatusCode);
        Assert.Empty((await resident.GetFromJsonAsync<SuggestionPage>(_path))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await resident.PutAsJsonAsync(itemPath, change with { Version = saved.Version })).StatusCode);

        await using AsyncServiceScope cleanup = factory.Services.CreateAsyncScope();
        // The job uses the application time and cancellation token only.
        await cleanup.ServiceProvider.GetRequiredService<SuggestionExpiryJob>().Execute(null!, CancellationToken.None);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM citizen_submissions.\"Suggestions\"", connection);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    private static async Task<HttpClient> LoginAsync(ApiFactory factory, string database, string name, bool confirmed)
    {
        const string password = "1234567!";
        string email = $"{name}@suggestion.test";
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AccountResponse account = await scope.ServiceProvider.GetRequiredService<AccountOperations>()
            .RegisterAsync(email, name, password, CancellationToken.None);
        if (confirmed)
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using var confirm = new NpgsqlCommand("UPDATE identity.\"AspNetUsers\" SET \"EmailConfirmed\" = true WHERE \"Id\" = @id", connection);
            confirm.Parameters.AddWithValue("id", account.Id);
            await confirm.ExecuteNonQueryAsync();
        }
        HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        await CsrfAsync(client);
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe = true });
        login.EnsureSuccessStatusCode();
        await CsrfAsync(client);
        return client;
    }

    private static async Task CsrfAsync(HttpClient client)
    {
        JsonElement response = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }
}
