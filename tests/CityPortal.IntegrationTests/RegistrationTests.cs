using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CityPortal.Api.Controllers;
using CityPortal.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class RegistrationTests : IClassFixture<RegistrationFixture>
{
    private readonly RegistrationFixture _fixture;

    public RegistrationTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RegisterConfirmAndLogout_WithCsrfAndCookie_EnforcesAccountLifecycleAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using HttpResponseMessage rejected = await client.PostAsJsonAsync("/api/auth/register", new { email = "alice@example.test", displayName = "Alice", password = "1234567!" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("invalid_csrf", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await CsrfAsync(client);
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/auth/register", new { email = " Alice@example.test ", displayName = " Alice ", password = "1234567!" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AccountResponse account = (await created.Content.ReadFromJsonAsync<AccountResponse>())!;
        Assert.Equal("Alice@example.test", account.Email);
        Assert.Equal("Alice", account.DisplayName);
        Assert.False(account.EmailConfirmed);
        Assert.Equal(["RegularUser"], account.Roles);
        Assert.Equal("Loading", account.ConfirmationDelivery.State);
        using HttpResponseMessage anonymousProfile = await client.GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousProfile.StatusCode);
        using HttpResponseMessage duplicate = await client.PostAsJsonAsync("/api/auth/register", new { email = "ALICE@EXAMPLE.TEST", displayName = "Other", password = "1234567!" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { email = "alice@example.test", password = "1234567!", rememberMe = false });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        string cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-CityPortal.Session="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        await CsrfAsync(client);
        using HttpResponseMessage profile = await client.GetAsync("/api/account");
        Assert.Equal(account.Id, (await profile.Content.ReadFromJsonAsync<AccountResponse>())!.Id);
        using HttpResponseMessage earlyResend = await client.PostAsync("/api/account/email-confirmation", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, earlyResend.StatusCode);
        Assert.InRange(earlyResend.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        (Guid link, string token) = await ReadLinkAsync(factory, database, account.Id);
        Assert.DoesNotContain(token, await created.Content.ReadAsStringAsync());
        using HttpResponseMessage confirm = await client.PostAsJsonAsync("/api/auth/confirm-email", new { linkId = link, token });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("Confirmed", (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using HttpResponseMessage repeat = await client.PostAsJsonAsync("/api/auth/confirm-email", new { linkId = link, token });
        Assert.Equal("AlreadyConfirmed", (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using HttpResponseMessage wrongToken = await client.PostAsJsonAsync("/api/auth/confirm-email", new { linkId = link, token = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongToken.StatusCode);
        using HttpResponseMessage confirmedProfile = await client.GetAsync("/api/account");
        Assert.True((await confirmedProfile.Content.ReadFromJsonAsync<AccountResponse>())!.EmailConfirmed);
        using HttpResponseMessage logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using HttpResponseMessage loggedOut = await client.GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
    }

    [Fact]
    public async Task RegistrationAndResend_WhenBrokerIsUnavailable_ReturnSuccessWithDurableWorkAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        using HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        await CsrfAsync(client);
        await _fixture.SetBrokerRunningAsync(false);
        try
        {
            const string password = "1234567!";
            using HttpResponseMessage created = await client.PostAsJsonAsync("/api/auth/register", new { email = "outage@example.test", displayName = "Outage", password });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            AccountResponse account = (await created.Content.ReadFromJsonAsync<AccountResponse>())!;
            Assert.False(account.EmailConfirmed);
            Assert.Equal("Loading", account.ConfirmationDelivery.State);
            using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password, rememberMe = false });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            await CsrfAsync(client);
            time.Advance(TimeSpan.FromSeconds(60));
            using HttpResponseMessage resent = await client.PostAsync("/api/account/email-confirmation", null);
            Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
            Assert.Equal("Loading", (await resent.Content.ReadFromJsonAsync<ConfirmationDeliveryResponse>())!.State);

            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var counts = new NpgsqlCommand("SELECT (SELECT count(*) FROM identity.\"AspNetUsers\"), (SELECT count(*) FROM identity.\"Links\" WHERE \"State\"='Active'), (SELECT count(*) FROM identity.\"Links\" WHERE \"State\"='Invalidated'), (SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"State\"='Queued'), (SELECT count(*) FROM notifications_messaging.wolverine_outgoing_envelopes)", connection))
            await using (NpgsqlDataReader reader = await counts.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
                Assert.Equal(2L, reader.GetInt64(3));
                Assert.Equal(2L, reader.GetInt64(4));
            }
            (_, string token) = await ReadLinkAsync(factory, database, account.Id);
            Assert.DoesNotContain(password, await created.Content.ReadAsStringAsync());
            Assert.DoesNotContain(token, await created.Content.ReadAsStringAsync());
            Assert.DoesNotContain(token, await resent.Content.ReadAsStringAsync());
            await using var envelopes = new NpgsqlCommand("SELECT encode(body, 'escape') FROM notifications_messaging.wolverine_outgoing_envelopes", connection);
            await using NpgsqlDataReader bodies = await envelopes.ExecuteReaderAsync();
            while (await bodies.ReadAsync())
            {
                string body = bodies.GetString(0);
                Assert.DoesNotContain(token, body);
                Assert.DoesNotContain(account.Email, body);
                Assert.DoesNotContain(password, body);
            }
        }
        finally
        {
            await _fixture.SetBrokerRunningAsync(true);
        }
    }

    [Fact]
    public async Task ProfileReads_WhenSessionIsRevokedAfterAuthentication_ReturnUnauthorizedAsync()
    {
        (ApiFactory factory, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AccountOperations accounts = scope.ServiceProvider.GetRequiredService<AccountOperations>();
        await accounts.RegisterAsync("revoked@example.test", "Revoked", "1234567!", CancellationToken.None);
        (AccountResponse account, Guid session, _) = await accounts.LoginAsync("revoked@example.test", "1234567!", false, CancellationToken.None);
        Assert.NotNull(await accounts.FindSessionAsync(session, CancellationToken.None));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("session", session.ToString()),
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString())
        ], "Cookies"));
        var controller = new AccountController(accounts)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
        await accounts.LogoutAsync(session, CancellationToken.None);

        ActionResult<AccountResponse> profile = await controller.GetAsync(CancellationToken.None);
        ActionResult<ConfirmationDeliveryResponse> delivery = await controller.GetConfirmationAsync(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(profile.Result);
        Assert.Null(profile.Value);
        Assert.IsType<UnauthorizedResult>(delivery.Result);
        Assert.Null(delivery.Value);
    }

    [Fact]
    public async Task RegistrationRateLimit_WithMalformedAndAlternateCaseRoutes_CountsAcrossRequestsAsync()
    {
        (ApiFactory factory, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await CsrfAsync(client);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2." + (attempt + 1));
            using var content = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage invalid = await client.PostAsync(attempt % 2 == 0 ? "/api/AUTH/REGISTER/" : "/api/auth/register", content);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using HttpResponseMessage limited = await client.PostAsJsonAsync("/api/Auth/Register", new { email = "new@example.test", displayName = "New", password = "1234567!" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 3590, 3600);
    }

    [Fact]
    public async Task Registration_WhenDeliveryInsertFails_RollsBackAccountRoleLinkAndOutboxAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using (var fail = new NpgsqlCommand("ALTER TABLE notifications.\"EmailDeliveries\" ADD CONSTRAINT reject_test_delivery CHECK (false)", connection))
            await fail.ExecuteNonQueryAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AccountOperations operations = scope.ServiceProvider.GetRequiredService<AccountOperations>();
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => operations.RegisterAsync("rollback@example.test", "Rollback", "1234567!", CancellationToken.None));
        await using var counts = new NpgsqlCommand("SELECT (SELECT count(*) FROM identity.\"AspNetUsers\") + (SELECT count(*) FROM identity.\"AspNetRoles\") + (SELECT count(*) FROM identity.\"AspNetUserRoles\") + (SELECT count(*) FROM identity.\"Links\") + (SELECT count(*) FROM notifications.\"EmailDeliveries\") + (SELECT count(*) FROM notifications_messaging.wolverine_outgoing_envelopes)", connection);
        Assert.Equal(0L, await counts.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Resend_WithReplacementQuotaAndExpiry_PreservesOnlyLatestLinkAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        AccountResponse account;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            account = await scope.ServiceProvider.GetRequiredService<AccountOperations>().RegisterAsync("resend@example.test", "Resend", "1234567!", CancellationToken.None);
        (Guid oldLink, string oldToken) = await ReadLinkAsync(factory, database, account.Id);
        time.Advance(TimeSpan.FromSeconds(60));
        await using (var connection = new NpgsqlConnection(database))
        {
            await connection.OpenAsync();
            await using (var fail = new NpgsqlCommand("ALTER TABLE notifications.\"EmailDeliveries\" ADD CONSTRAINT reject_replacement CHECK (\"Id\" = '" + account.Id + "') NOT VALID", connection))
                await fail.ExecuteNonQueryAsync();
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
                await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<AccountOperations>().ResendAsync(account.Id, CancellationToken.None));
            await using (var read = new NpgsqlCommand("SELECT \"State\" FROM identity.\"Links\" WHERE \"Id\" = @id", connection))
            {
                read.Parameters.AddWithValue("id", oldLink);
                Assert.Equal("Active", await read.ExecuteScalarAsync());
            }
            await using var restore = new NpgsqlCommand("ALTER TABLE notifications.\"EmailDeliveries\" DROP CONSTRAINT reject_replacement", connection);
            await restore.ExecuteNonQueryAsync();
        }
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().ResendAsync(account.Id, CancellationToken.None);
        (Guid latest, string token) = await ReadLinkAsync(factory, database, account.Id);
        Assert.NotEqual(oldLink, latest);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AccountOperationException invalidated = await Assert.ThrowsAsync<AccountOperationException>(() => scope.ServiceProvider.GetRequiredService<AccountOperations>().ConfirmAsync(oldLink, oldToken, CancellationToken.None));
            Assert.Equal("invalid_link", invalidated.Code);
        }
        for (int replacement = 0; replacement < 3; replacement++)
        {
            time.Advance(TimeSpan.FromSeconds(60));
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().ResendAsync(account.Id, CancellationToken.None);
        }
        time.Advance(TimeSpan.FromSeconds(60));
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AccountOperationException quota = await Assert.ThrowsAsync<AccountOperationException>(() => scope.ServiceProvider.GetRequiredService<AccountOperations>().ResendAsync(account.Id, CancellationToken.None));
            Assert.Equal(429, quota.Status);
            Assert.Equal(3300, quota.RetryAfterSeconds);
        }
        (latest, token) = await ReadLinkAsync(factory, database, account.Id);
        time.Advance(TimeSpan.FromHours(24));
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AccountOperationException expired = await Assert.ThrowsAsync<AccountOperationException>(() => scope.ServiceProvider.GetRequiredService<AccountOperations>().ConfirmAsync(latest, token, CancellationToken.None));
            Assert.Equal("expired_link", expired.Code);
        }
    }

    [Fact]
    public async Task Confirmation_WhenSubmittedConcurrently_ConsumesOnceAndReturnsInformationalReplayAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse account;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            account = await scope.ServiceProvider.GetRequiredService<AccountOperations>().RegisterAsync("concurrent@example.test", "Concurrent", "1234567!", CancellationToken.None);
        (Guid link, string token) = await ReadLinkAsync(factory, database, account.Id);
        async Task<string> ConsumeAsync()
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AccountOperations>().ConfirmAsync(link, token, CancellationToken.None);
        }
        string[] outcomes = await Task.WhenAll(ConsumeAsync(), ConsumeAsync());
        Assert.Equal(["AlreadyConfirmed", "Confirmed"], outcomes.Order());
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT \"Version\" FROM identity.\"AspNetUsers\" WHERE \"Id\" = @id", connection);
        query.Parameters.AddWithValue("id", account.Id);
        Assert.Equal(2L, await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Login_WithLockoutAbsoluteExpiryAndLogout_ValidatesPersistedSessionsAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, _) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AccountOperations>().RegisterAsync("session@example.test", "Session", "1234567!", CancellationToken.None);
        for (int failure = 0; failure < 5; failure++)
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            AccountOperationException invalid = await Assert.ThrowsAsync<AccountOperationException>(() => scope.ServiceProvider.GetRequiredService<AccountOperations>().LoginAsync("session@example.test", "wrong", false, CancellationToken.None));
            Assert.Equal(failure == 4 ? 429 : 401, invalid.Status);
        }
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AccountOperationException locked = await Assert.ThrowsAsync<AccountOperationException>(() => scope.ServiceProvider.GetRequiredService<AccountOperations>().LoginAsync("session@example.test", "1234567!", false, CancellationToken.None));
            Assert.Equal(900, locked.RetryAfterSeconds);
        }
        time.Advance(TimeSpan.FromMinutes(15));
        Guid normalSession;
        Guid rememberedSession;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            (AccountResponse _, Guid session, DateTimeOffset expires) = await scope.ServiceProvider.GetRequiredService<AccountOperations>().LoginAsync("session@example.test", "1234567!", false, CancellationToken.None);
            normalSession = session;
            Assert.Equal(time.GetUtcNow().AddHours(12), expires);
        }
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            (AccountResponse _, Guid session, DateTimeOffset expires) = await scope.ServiceProvider.GetRequiredService<AccountOperations>().LoginAsync("session@example.test", "1234567!", true, CancellationToken.None);
            rememberedSession = session;
            Assert.Equal(time.GetUtcNow().AddDays(30), expires);
        }
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AccountOperations operations = scope.ServiceProvider.GetRequiredService<AccountOperations>();
            (_, Guid temporary, _) = await operations.LoginAsync("session@example.test", "1234567!", false, CancellationToken.None);
            await operations.LogoutAsync(temporary, CancellationToken.None);
            Assert.Null(await operations.FindSessionAsync(temporary, CancellationToken.None));
            Assert.NotNull(await operations.FindSessionAsync(normalSession, CancellationToken.None));
            Assert.NotNull(await operations.FindSessionAsync(rememberedSession, CancellationToken.None));
        }
        time.Advance(TimeSpan.FromHours(12));
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AccountOperations operations = scope.ServiceProvider.GetRequiredService<AccountOperations>();
            Assert.Null(await operations.FindSessionAsync(normalSession, CancellationToken.None));
            Assert.Equal("session@example.test", (await operations.FindSessionAsync(rememberedSession, CancellationToken.None))!.Email);
            await operations.LogoutAsync(rememberedSession, CancellationToken.None);
            Assert.Null(await operations.FindSessionAsync(rememberedSession, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RequestQuota_WhenContextsCompete_AllowsExactlyTheConfiguredCountAsync()
    {
        (ApiFactory factory, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        async Task<int> AttemptAsync()
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<AccountOperations>().EnforceIpLimitAsync("192.0.2.1", "registration", 5, TimeSpan.FromHours(1), CancellationToken.None);
                return 200;
            }
            catch (AccountOperationException exception)
            {
                return exception.Status;
            }
        }
        int[] results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => AttemptAsync()));
        Assert.Equal(5, results.Count(status => status == 200));
        Assert.Equal(1, results.Count(status => status == 429));
    }

    private static async Task CsrfAsync(HttpClient client)
    {
        JsonElement response = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }

    private static async Task<(Guid Id, string Token)> ReadLinkAsync(ApiFactory factory, string database, Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT \"LinkId\", \"ProtectedPayload\" FROM notifications.\"EmailDeliveries\" WHERE \"AccountId\" = @account ORDER BY \"CreatedAtUtc\" DESC LIMIT 1", connection);
        query.Parameters.AddWithValue("account", accountId);
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Guid link = reader.GetGuid(0);
        string protectedPayload = reader.GetString(1);
        string json = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("CityPortal.AccountMail.v1").Unprotect(protectedPayload);
        using var payload = JsonDocument.Parse(json);
        string token = payload.RootElement.GetProperty("Token").GetString()!;
        Assert.DoesNotContain(token, protectedPayload);
        return (link, token);
    }
}
