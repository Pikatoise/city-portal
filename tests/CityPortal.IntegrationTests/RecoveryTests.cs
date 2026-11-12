using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class RecoveryTests : IClassFixture<RegistrationFixture>
{
    private readonly RegistrationFixture _fixture;

    public RecoveryTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProfileEdits_WithVersionAndCurrentPassword_EnforcePermissionsAndRevokeEverySessionAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse account = await RunAsync(factory, operations => operations.RegisterAsync("profile@example.test", "Profile", "1234567!", CancellationToken.None));
        using HttpClient first = Client(factory);
        using HttpClient second = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PatchAsJsonAsync("/api/account/display-name", new { displayName = "Updated", version = 1 })).StatusCode);
        await LoginAsync(first, account.Email);
        await LoginAsync(second, account.Email);
        await CsrfAsync(first);
        using HttpResponseMessage unconfirmed = await first.PatchAsJsonAsync("/api/account/display-name", new { displayName = "Updated", version = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, unconfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PostAsJsonAsync("/api/account/change-password", new { currentPassword = "1234567!", newPassword = "Changed1!", version = 1 })).StatusCode);
        (Guid id, string token) = await ReadLinkAsync(factory, database, "confirmation");
        await RunAsync(factory, operations => operations.ConfirmAsync(id, token, CancellationToken.None));
        using HttpResponseMessage stale = await first.PatchAsJsonAsync("/api/account/display-name", new { displayName = "Updated", version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using HttpResponseMessage changed = await first.PatchAsJsonAsync("/api/account/display-name", new { displayName = "  Житель2  ", version = 2 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        AccountResponse updated = (await changed.Content.ReadFromJsonAsync<AccountResponse>())!;
        Assert.Equal("Житель2", updated.DisplayName);
        Assert.Equal(3, updated.Version);
        using HttpResponseMessage wrong = await first.PostAsJsonAsync("/api/account/change-password", new { currentPassword = "wrong", newPassword = "Changed1!", version = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("invalid_current_password", (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/account")).StatusCode);
        first.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using HttpResponseMessage csrf = await first.PostAsJsonAsync("/api/account/change-password", new { currentPassword = "1234567!", newPassword = "Changed1!", version = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        await CsrfAsync(first);
        using HttpResponseMessage password = await first.PostAsJsonAsync("/api/account/change-password", new { currentPassword = "1234567!", newPassword = "Changed1!", version = 3 });
        Assert.Equal(HttpStatusCode.NoContent, password.StatusCode);
        Assert.Contains(password.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-CityPortal.Session=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/account")).StatusCode);
        await CsrfAsync(first);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = "1234567!", rememberMe = false })).StatusCode);
        await LoginAsync(first, account.Email, "Changed1!");
        Assert.Equal(4, (await first.GetFromJsonAsync<AccountResponse>("/api/account"))!.Version);
    }

    [Fact]
    public async Task Recovery_WithReplacementExpiryAndConcurrentConsumption_PreservesConfirmationAndHasOneWinnerAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        AccountResponse account = await RunAsync(factory, operations => operations.RegisterAsync("recovery@example.test", "Recovery", "1234567!", CancellationToken.None));
        (Guid confirmation, string confirmationToken) = await ReadLinkAsync(factory, database, "confirmation");
        using HttpClient client = Client(factory);
        await LoginAsync(client, account.Email);
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = " RECOVERY@example.test " })).StatusCode);
        (Guid oldId, string oldToken) = await ReadLinkAsync(factory, database, "recovery");
        AccountOperationException wrongPurpose = await Assert.ThrowsAsync<AccountOperationException>(() => RunAsync(factory, operations => operations.ConfirmAsync(oldId, oldToken, CancellationToken.None)));
        Assert.Equal("invalid_link", wrongPurpose.Code);
        time.Advance(TimeSpan.FromSeconds(60));
        await RunAsync(factory, async operations => { await operations.ForgotPasswordAsync(account.Email, CancellationToken.None); return true; });
        (Guid active, string secret) = await ReadLinkAsync(factory, database, "recovery");
        Assert.NotEqual(oldId, active);
        AccountOperationException replaced = await Assert.ThrowsAsync<AccountOperationException>(() => ResetAsync(factory, oldId, oldToken));
        Assert.Equal("invalid_link", replaced.Code);
        AccountOperationException wrongToken = await Assert.ThrowsAsync<AccountOperationException>(() => ResetAsync(factory, active, "incorrect"));
        Assert.Equal("invalid_link", wrongToken.Code);
        AccountOperationException confirmationAsRecovery = await Assert.ThrowsAsync<AccountOperationException>(() => ResetAsync(factory, confirmation, confirmationToken));
        Assert.Equal("invalid_link", confirmationAsRecovery.Code);
        time.Advance(TimeSpan.FromHours(1));
        AccountOperationException expired = await Assert.ThrowsAsync<AccountOperationException>(() => ResetAsync(factory, active, secret));
        Assert.Equal("expired_link", expired.Code);
        await RunAsync(factory, async operations => { await operations.ForgotPasswordAsync(account.Email, CancellationToken.None); return true; });
        (Guid finalId, string finalToken) = await ReadLinkAsync(factory, database, "recovery");
        async Task<HttpStatusCode> ResetRequestAsync()
        {
            using HttpClient anonymous = Client(factory);
            await CsrfAsync(anonymous);
            return (await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { linkId = finalId, token = finalToken, newPassword = "Recovered1!" })).StatusCode;
        }
        HttpStatusCode[] results = await Task.WhenAll(ResetRequestAsync(), ResetRequestAsync());
        Assert.Equal(1, results.Count(status => status == HttpStatusCode.NoContent));
        Assert.Equal(1, results.Count(status => status == HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account")).StatusCode);
        await LoginAsync(client, account.Email, "Recovered1!");
        Assert.False((await client.GetFromJsonAsync<AccountResponse>("/api/account"))!.EmailConfirmed);
        Assert.Equal("Confirmed", await RunAsync(factory, operations => operations.ConfirmAsync(confirmation, confirmationToken, CancellationToken.None)));
    }

    [Fact]
    public async Task Recovery_ForUnknownAndSuppressedAccounts_ReturnsSameResponseAndSeparatesMailQuotasAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        await RunAsync(factory, operations => operations.RegisterAsync("quota@example.test", "Quota", "1234567!", CancellationToken.None));
        using HttpClient client = Client(factory);
        await CsrfAsync(client);
        using HttpResponseMessage unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "missing@example.test" });
        using HttpResponseMessage known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "quota@example.test" });
        using HttpResponseMessage suppressed = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "quota@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, known.StatusCode);
        Assert.Equal(unknown.StatusCode, suppressed.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());
        Assert.Equal(await known.Content.ReadAsStringAsync(), await suppressed.Content.ReadAsStringAsync());
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"Links\" WHERE \"Purpose\"='recovery'"));
        for (int index = 1; index < 5; index++)
        {
            time.Advance(TimeSpan.FromSeconds(60));
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "quota@example.test" })).StatusCode);
        }
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "quota@example.test" })).StatusCode);
        Assert.Equal(5L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"Links\" WHERE \"Purpose\"='recovery'"));
        await LoginAsync(client, "quota@example.test");
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/account/email-confirmation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "missing@example.test" })).StatusCode);
        // Nine recovery requests plus confirmation share the ten-request IP window.
        using HttpResponseMessage limited = await client.PostAsJsonAsync("/api/AUTH/FORGOT-PASSWORD/", new { email = "missing@example.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(2L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"Links\" WHERE \"Purpose\"='confirmation'"));
    }

    [Fact]
    public async Task Recovery_WhenQueueWriteFails_RollsBackReplacementAndPasswordResetFailuresPreserveLinkAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        await RunAsync(factory, operations => operations.RegisterAsync("rollback@example.test", "Rollback", "1234567!", CancellationToken.None));
        await RunAsync(factory, async operations => { await operations.ForgotPasswordAsync("rollback@example.test", CancellationToken.None); return true; });
        (Guid id, string token) = await ReadLinkAsync(factory, database, "recovery");
        (_, Guid session, _) = await RunAsync(factory, operations => operations.LoginAsync("rollback@example.test", "1234567!", false, CancellationToken.None));
        time.Advance(TimeSpan.FromSeconds(60));
        await ExecuteAsync(database, "ALTER TABLE notifications.\"EmailDeliveries\" ADD CONSTRAINT reject_recovery CHECK (\"Purpose\" <> 'recovery') NOT VALID");
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => RunAsync(factory, async operations => { await operations.ForgotPasswordAsync("rollback@example.test", CancellationToken.None); return true; }));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"Links\" WHERE \"Purpose\"='recovery' AND \"State\"='Active'"));
        AccountOperationException invalidPassword = await Assert.ThrowsAsync<AccountOperationException>(() => ResetAsync(factory, id, token, "no punctuation"));
        Assert.Equal("validation_failed", invalidPassword.Code);
        await ExecuteAsync(database, "ALTER TABLE identity.\"AspNetUsers\" ADD CONSTRAINT reject_password CHECK (\"Version\"=1) NOT VALID");
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => ResetAsync(factory, id, token));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"Links\" WHERE \"Purpose\"='recovery' AND \"State\"='Active'"));
        Assert.NotNull(await RunAsync(factory, operations => operations.FindSessionAsync(session, CancellationToken.None)));
        await ExecuteAsync(database, "ALTER TABLE identity.\"AspNetUsers\" DROP CONSTRAINT reject_password");
        await ResetAsync(factory, id, token);
        Assert.Null(await RunAsync(factory, operations => operations.FindSessionAsync(session, CancellationToken.None)));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"Links\" WHERE \"Purpose\"='recovery' AND \"State\"='Consumed'"));
    }

    [Fact]
    public async Task ProfileEdits_WhenConcurrent_KeepFirstVersionAndRejectStaleOrRevokedWritersAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse account = await RunAsync(factory, operations => operations.RegisterAsync("concurrent@example.test", "Concurrent", "1234567!", CancellationToken.None));
        (Guid link, string token) = await ReadLinkAsync(factory, database, "confirmation");
        await RunAsync(factory, operations => operations.ConfirmAsync(link, token, CancellationToken.None));
        (_, Guid session, _) = await RunAsync(factory, operations => operations.LoginAsync(account.Email, "1234567!", false, CancellationToken.None));
        async Task<int> EditAsync(string name)
        {
            try
            {
                await RunAsync(factory, operations => operations.ChangeDisplayNameAsync(account.Id, session, name, 2, CancellationToken.None));
                return 200;
            }
            catch (AccountOperationException exception) { return exception.Status; }
        }
        int[] results = await Task.WhenAll(EditAsync("First"), EditAsync("Second"));
        Assert.Equal(1, results.Count(status => status == 200));
        Assert.Equal(1, results.Count(status => status == 409));
        AccountResponse current = (await RunAsync(factory, operations => operations.FindSessionAsync(session, CancellationToken.None)))!;
        Assert.Equal(3, current.Version);
        Assert.Contains(current.DisplayName, new[] { "First", "Second" });
        await RunAsync(factory, async operations => { await operations.LogoutAsync(session, CancellationToken.None); return true; });
        AccountOperationException revoked = await Assert.ThrowsAsync<AccountOperationException>(() => RunAsync(factory, operations => operations.ChangeDisplayNameAsync(account.Id, session, "Forbidden", 3, CancellationToken.None)));
        Assert.Equal(401, revoked.Status);
    }

    private static HttpClient Client(ApiFactory factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    [Fact]
    public async Task DisplayName_WhenLoginCompetes_SerializesBeforeCredentialSessionWriteAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse account = await RunAsync(factory, operations => operations.RegisterAsync("race@example.test", "Before", "1234567!", CancellationToken.None));
        (Guid link, string token) = await ReadLinkAsync(factory, database, "confirmation");
        await RunAsync(factory, operations => operations.ConfirmAsync(link, token, CancellationToken.None));
        (_, Guid session, _) = await RunAsync(factory, operations => operations.LoginAsync(account.Email, "1234567!", false, CancellationToken.None));
        await using var blocker = new NpgsqlConnection(database);
        await blocker.OpenAsync();
        await using NpgsqlTransaction transaction = await blocker.BeginTransactionAsync();
        await using var accountLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", blocker, transaction);
        accountLock.Parameters.AddWithValue("key", "account:" + account.Id);
        await accountLock.ExecuteNonQueryAsync();
        Task<AccountResponse> edit = RunAsync(factory, operations => operations.ChangeDisplayNameAsync(account.Id, session, "After", 2, CancellationToken.None));
        await using var observer = new NpgsqlConnection(database);
        await observer.OpenAsync();
        await using var emailLock = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))", observer);
        emailLock.Parameters.AddWithValue("key", "email:RACE@EXAMPLE.TEST");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // The edit must own the login lock before waiting for the account lock.
            while ((bool)(await emailLock.ExecuteScalarAsync(timeout.Token))!)
                await Task.Delay(10, timeout.Token);
        }
        finally
        {
            await transaction.CommitAsync();
        }
        Task<(AccountResponse Account, Guid SessionId, DateTimeOffset ExpiresAtUtc)> login = RunAsync(factory, operations => operations.LoginAsync(account.Email, "1234567!", false, CancellationToken.None));
        Assert.Equal("After", (await edit).DisplayName);
        Assert.Equal("After", (await login).Account.DisplayName);
    }

    private static async Task LoginAsync(HttpClient client, string email, string password = "1234567!")
    {
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe = false })).StatusCode);
    }

    private static async Task CsrfAsync(HttpClient client)
    {
        JsonElement csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task<T> RunAsync<T>(ApiFactory factory, Func<AccountOperations, Task<T>> action)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccountOperations>());
    }

    private static async Task ResetAsync(ApiFactory factory, Guid id, string token, string password = "Recovered1!")
    {
        await RunAsync(factory, async operations => { await operations.ResetPasswordAsync(id, token, password, CancellationToken.None); return true; });
    }

    private static async Task<(Guid Id, string Token)> ReadLinkAsync(ApiFactory factory, string database, string purpose)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT \"LinkId\", \"ProtectedPayload\" FROM notifications.\"EmailDeliveries\" WHERE \"Purpose\"=@purpose ORDER BY \"CreatedAtUtc\" DESC LIMIT 1", connection);
        query.Parameters.AddWithValue("purpose", purpose);
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        string json = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("CityPortal.AccountMail.v1").Unprotect(reader.GetString(1));
        using var payload = JsonDocument.Parse(json);
        return (reader.GetGuid(0), payload.RootElement.GetProperty("Token").GetString()!);
    }

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
