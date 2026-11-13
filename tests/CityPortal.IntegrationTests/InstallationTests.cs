using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class InstallationTests : IClassFixture<RegistrationFixture>
{
    private const string _password = "InstallerPass!123";
    private readonly RegistrationFixture _fixture;

    public InstallationTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Setup_InPublicMode_RejectsCompletionRegardlessOfForwardedLoopbackAddressAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync(installed: false);
        await using ApiFactory lifetime = factory;
        await using WebApplicationFactory<CityPortal.Api.Program> configured = factory.WithWebHostBuilder(builder => builder.UseSetting("Installation:Enabled", "false"));
        using HttpClient client = Client(configured);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        await CsrfAsync(client);
        await using var smtp = new DeliveryIntegrationTests.TestSmtp();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/installation/complete", Request(smtp.Port));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("installation_disabled", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/installation/status")).GetProperty("canInstall").GetBoolean());
        Assert.Equal(0, smtp.ConnectionCount);
        Assert.Equal(0L, await CountAsync(database));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_InPublicMode_BecomesReadyOnlyAfterInstallationWhileLivenessStaysHealthyAsync(bool installed)
    {
        (ApiFactory factory, _) = await _fixture.CreateAsync(installed: installed);
        await using ApiFactory lifetime = factory;
        await using WebApplicationFactory<CityPortal.Api.Program> configured = factory.WithWebHostBuilder(builder => builder.UseSetting("Installation:Enabled", "false"));
        using HttpClient client = Client(configured);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(installed ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        JsonElement status = await client.GetFromJsonAsync<JsonElement>("/api/installation/status");
        Assert.Equal(installed, status.GetProperty("installed").GetBoolean());
        Assert.False(status.GetProperty("canInstall").GetBoolean());
    }

    [Fact]
    public async Task Setup_WhenPending_RequiresCsrfAndExplicitCompletionAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync(installed: false);
        await using ApiFactory lifetime = factory;
        await using WebApplicationFactory<CityPortal.Api.Program> configured = Configure(factory);
        using HttpClient client = Client(configured);
        JsonElement status = await client.GetFromJsonAsync<JsonElement>("/api/installation/status");
        Assert.False(status.GetProperty("installed").GetBoolean());
        Assert.True(status.GetProperty("canInstall").GetBoolean());
        Assert.Equal(2, status.EnumerateObject().Count());
        await using var smtp = new DeliveryIntegrationTests.TestSmtp();
        object request = Request(smtp.Port);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/installation/complete", request)).StatusCode);
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", new { email = "resident@example.test", displayName = "Resident", password = _password })).StatusCode);
        Assert.Equal(0, smtp.ConnectionCount);
        Assert.Equal(0L, await CountAsync(database));
    }

    [Fact]
    public async Task Setup_WhenConcurrent_CreatesOneActiveAdministratorAndPersistsPublicAndProtectedDefaultsAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync(installed: false);
        await using ApiFactory lifetime = factory;
        await using WebApplicationFactory<CityPortal.Api.Program> configured = Configure(factory);
        using HttpClient first = Client(configured);
        using HttpClient second = Client(configured);
        await CsrfAsync(first);
        await CsrfAsync(second);
        await using var smtp = new DeliveryIntegrationTests.TestSmtp();
        HttpResponseMessage[] responses = await Task.WhenAll(first.PostAsJsonAsync("/api/installation/complete", Request(smtp.Port)), second.PostAsJsonAsync("/api/installation/complete", Request(smtp.Port)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(smtp.Accepted);
        Assert.Equal(4L, await CountAsync(database));
        JsonElement metadata = await first.GetFromJsonAsync<JsonElement>("/api/system/info");
        Assert.Equal("TestPortal", metadata.GetProperty("name").GetString());
        Assert.Equal("TestCity", metadata.GetProperty("city").GetString());
        Assert.Equal("TestRegion", metadata.GetProperty("region").GetString());
        Assert.Equal("Asia/Yekaterinburg", metadata.GetProperty("timeZone").GetString());
        Assert.DoesNotContain("smtp", metadata.GetRawText(), StringComparison.OrdinalIgnoreCase);
        await CsrfAsync(first);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/auth/login", new { email = "installer@example.test", password = _password, rememberMe = false })).StatusCode);
        JsonElement profile = await first.GetFromJsonAsync<JsonElement>("/api/account");
        Assert.True(profile.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(new[] { "Administrator", "RegularUser" }, profile.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Order().ToArray());
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/admin/accounts")).StatusCode);
        await using AsyncServiceScope scope = configured.Services.CreateAsyncScope();
        AccountMailOptions saved = await scope.ServiceProvider.GetRequiredService<MailConfigurationStore>().GetAsync(CancellationToken.None);
        Assert.Equal(smtp.Port, saved.SmtpPort);
        Assert.Equal("smtp-secret-for-storage", saved.Password);
        Assert.Equal("TestPortal", saved.PortalName);
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT \"ProtectedOptions\" FROM notifications.\"MailConfiguration\"", connection);
        string protectedOptions = Assert.IsType<string>(await query.ExecuteScalarAsync());
        Assert.DoesNotContain("smtp-secret-for-storage", protectedOptions);
        Assert.DoesNotContain("127.0.0.1", protectedOptions);
        // A new service scope/process reads persisted configuration rather than a startup snapshot.
        await using AsyncServiceScope nextScope = configured.Services.CreateAsyncScope();
        Assert.Equal(smtp.Port, (await nextScope.ServiceProvider.GetRequiredService<MailConfigurationStore>().GetAsync(CancellationToken.None)).SmtpPort);
    }

    [Theory]
    [InlineData("smtp")]
    [InlineData("storage")]
    public async Task Setup_WhenSmtpOrStorageFails_RollsBackAllInstallationWritesAndCanRetryAsync(string failure)
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync(installed: false);
        await using ApiFactory lifetime = factory;
        await using WebApplicationFactory<CityPortal.Api.Program> configured = Configure(factory);
        using HttpClient client = Client(configured);
        await CsrfAsync(client);
        await using var smtp = new DeliveryIntegrationTests.TestSmtp();
        if (failure == "smtp")
            smtp.RejectionCode = 550;
        else
            await ExecuteAsync(database, "CREATE FUNCTION notifications.reject_setup() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test storage rejection'; END $$; CREATE TRIGGER reject_setup BEFORE INSERT ON notifications.\"MailConfiguration\" FOR EACH ROW EXECUTE FUNCTION notifications.reject_setup();");
        HttpResponseMessage failed = await client.PostAsJsonAsync("/api/installation/complete", Request(smtp.Port));
        Assert.Equal(failure == "smtp" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("smtp-secret-for-storage", await failed.Content.ReadAsStringAsync());
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/installation/status")).GetProperty("installed").GetBoolean());
        Assert.Equal(0L, await CountAsync(database));
        smtp.RejectionCode = 0;
        if (failure == "storage")
            await ExecuteAsync(database, "DROP TRIGGER reject_setup ON notifications.\"MailConfiguration\";");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/installation/complete", Request(smtp.Port))).StatusCode);
        Assert.Equal(4L, await CountAsync(database));
    }

    [Theory]
    [InlineData("timeZone", "invalid/zone")]
    [InlineData("password", "weak")]
    [InlineData("name", "")]
    public async Task Setup_WithInvalidInput_DoesNotSendMailOrCreateAccountsAsync(string field, string value)
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync(installed: false);
        await using ApiFactory lifetime = factory;
        await using WebApplicationFactory<CityPortal.Api.Program> configured = Configure(factory);
        using HttpClient client = Client(configured);
        await CsrfAsync(client);
        await using var smtp = new DeliveryIntegrationTests.TestSmtp();
        Dictionary<string, JsonElement> request = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(Request(smtp.Port)))
            ?? throw new InvalidOperationException("The serialized test request must be an object.");
        request[field] = JsonSerializer.SerializeToElement(value);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/installation/complete", request)).StatusCode);
        Assert.Equal(0, smtp.ConnectionCount);
        Assert.Equal(0L, await CountAsync(database));
    }

    [Fact]
    public async Task SetupMigration_WithExistingAccounts_PreservesAccountsAndClosesWizardAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AccountResponse account = await scope.ServiceProvider.GetRequiredService<AccountOperations>().RegisterAsync("legacy@example.test", "LegacyUser", _password, CancellationToken.None);
        await ExecuteAsync(database, "DROP TABLE identity.\"SiteConfiguration\"; DELETE FROM identity.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_SiteInstallation';");
        await factory.Services.MigrateIdentityAsync(CancellationToken.None);
        InstallationInfo site = await scope.ServiceProvider.GetRequiredService<SiteInstallation>().GetAsync(CancellationToken.None);
        Assert.True(site.Installed);
        Assert.Null(site.Name);
        Assert.Null(site.TimeZone);
        Assert.Equal(account.Id, (await scope.ServiceProvider.GetRequiredService<AccountOperations>().LoginAsync(account.Email, _password, false, CancellationToken.None)).Account.Id);
    }

    private static WebApplicationFactory<CityPortal.Api.Program> Configure(ApiFactory factory) => factory.WithWebHostBuilder(builder => builder.UseSetting("Installation:Enabled", "true"));

    private static HttpClient Client(WebApplicationFactory<CityPortal.Api.Program> factory)
    {
        HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        return client;
    }

    private static object Request(int port) => new
    {
        email = "installer@example.test",
        displayName = "Installer",
        password = _password,
        name = "TestPortal",
        city = "TestCity",
        region = "TestRegion",
        timeZone = "Asia/Yekaterinburg",
        smtpHost = "127.0.0.1",
        smtpPort = port,
        senderAddress = "noreply@example.test",
        useTls = false,
        smtpPassword = "smtp-secret-for-storage"
    };

    private static async Task CsrfAsync(HttpClient client)
    {
        JsonElement csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT (SELECT count(*) FROM identity.\"SiteConfiguration\") + (SELECT count(*) FROM identity.\"AspNetUsers\") + (SELECT count(*) FROM identity.\"RoleChanges\") + (SELECT count(*) FROM notifications.\"MailConfiguration\")", connection);
        return Assert.IsType<long>(await query.ExecuteScalarAsync());
    }
}
