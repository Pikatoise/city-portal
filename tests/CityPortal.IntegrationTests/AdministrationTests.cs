using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class AdministrationTests : IClassFixture<RegistrationFixture>
{
    private const string _password = "Password1!";
    private readonly RegistrationFixture _fixture;

    public AdministrationTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Bootstrap_WithMissingUnconfirmedOrExistingAdministrator_RefusesAndAuditsOnlySuccessfulSetupAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse account = await SeedAsync(factory, database, "bootstrap@example.test", false);
        Assert.Equal(404, (await Assert.ThrowsAsync<AccountOperationException>(() => AdminAsync(factory, admin => admin.BootstrapAsync("missing@example.test", CancellationToken.None)))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AccountOperationException>(() => AdminAsync(factory, admin => admin.BootstrapAsync(account.Email, CancellationToken.None)))).Status);
        await ConfirmAsync(factory, database, account.Id);
        Assert.Equal(0, await BootstrapCommandAsync(factory, database, " BOOTSTRAP@example.test "));
        (_, Guid session, _) = await LoginAsync(factory, account.Email);
        RoleHistoryPage history = await AdminAsync(factory, admin => admin.HistoryAsync(account.Id, session, account.Id, 1, 20, CancellationToken.None));
        RoleHistoryItem change = Assert.Single(history.Items);
        Assert.Equal("Bootstrap", change.ActorKind);
        Assert.Null(change.ActorId);
        Assert.Equal(account.Id, change.TargetAccountId);
        Assert.Equal(["RegularUser"], change.OldRoles);
        Assert.Equal(["Administrator", "RegularUser"], change.NewRoles);
        AccountOperationException repeat = await Assert.ThrowsAsync<AccountOperationException>(() => AdminAsync(factory, admin => admin.BootstrapAsync(account.Email, CancellationToken.None)));
        Assert.Equal("administrator_exists", repeat.Code);
        Assert.Equal(1, await BootstrapCommandAsync(factory, database, account.Email));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"RoleChanges\""));
    }

    [Fact]
    public async Task Bootstrap_WhenConcurrent_ProvisionsExactlyOneInitialAdministratorAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse first = await SeedAsync(factory, database, "first@example.test");
        AccountResponse second = await SeedAsync(factory, database, "second@example.test");
        async Task<int> AttemptAsync(string email)
        {
            try
            { await AdminAsync(factory, admin => admin.BootstrapAsync(email, CancellationToken.None)); return 200; }
            catch (AccountOperationException exception) { return exception.Status; }
        }
        int[] results = await Task.WhenAll(AttemptAsync(first.Email), AttemptAsync(second.Email));
        Assert.Equal(1, results.Count(status => status == 200));
        Assert.Equal(1, results.Count(status => status == 409));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"AspNetUserRoles\" ur JOIN identity.\"AspNetRoles\" r ON r.\"Id\"=ur.\"RoleId\" WHERE r.\"Name\"='Administrator'"));
    }

    [Fact]
    public async Task AdministrationHttp_WithSearchRolesVersionsAndCsrf_EnforcesMetadataContractsAndPermissionsAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse actor = await SeedAsync(factory, database, "administrator@example.test");
        AccountResponse target = await SeedAsync(factory, database, "writer@example.test");
        AccountResponse unconfirmed = await SeedAsync(factory, database, "unconfirmed@example.test", false);
        await AdminAsync(factory, admin => admin.BootstrapAsync(actor.Email, CancellationToken.None));
        using HttpClient client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/accounts")).StatusCode);
        await HttpLoginAsync(client, target.Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/accounts")).StatusCode);
        await HttpLoginAsync(client, actor.Email);
        AccountPage page = (await client.GetFromJsonAsync<AccountPage>("/api/admin/accounts?query=WRITER&emailConfirmed=true"))!;
        Assert.Equal(target.Id, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        AccountSummary details = (await client.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{target.Id}"))!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/roles", new { roles = new[] { "Author", "Editor" }, version = details.Version })).StatusCode);
        await CsrfAsync(client);
        using HttpResponseMessage granted = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/roles", new { roles = new[] { "Author", "Editor" }, version = details.Version });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        AccountSummary updated = (await granted.Content.ReadFromJsonAsync<AccountSummary>())!;
        Assert.Equal(["Author", "Editor", "RegularUser"], updated.Roles);
        Assert.Equal(details.Version + 1, updated.Version);
        string json = await granted.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedPayload", json);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/roles", new { roles = Array.Empty<string>(), version = details.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/roles", new { roles = new[] { "SuperUser" }, version = updated.Version })).StatusCode);
        RoleHistoryPage history = (await client.GetFromJsonAsync<RoleHistoryPage>($"/api/admin/accounts/{target.Id}/role-history"))!;
        RoleHistoryItem change = Assert.Single(history.Items);
        Assert.Equal(actor.Id, change.ActorId);
        Assert.Equal(target.Id, change.TargetAccountId);
        Assert.Equal(updated.Roles, change.NewRoles);
        Assert.Equal(["RegularUser"], change.OldRoles);
        Assert.Equal(target.Id, Assert.Single((await client.GetFromJsonAsync<AccountPage>("/api/admin/accounts?role=Author"))!.Items).Id);
        Assert.Equal(unconfirmed.Id, Assert.Single((await client.GetFromJsonAsync<AccountPage>("/api/admin/accounts?emailConfirmed=false"))!.Items).Id);
        Assert.Single((await client.GetFromJsonAsync<AccountPage>("/api/admin/accounts?pageSize=1&page=2"))!.Items);
        Assert.Empty((await client.GetFromJsonAsync<AccountPage>("/api/admin/accounts?query=NoSuchAccount"))!.Items);
        foreach (string invalid in new[] { "page=0", "pageSize=101", "page=2147483647", "role=Unknown", "query=" + new string('a', 255) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/accounts?" + invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/accounts/{Guid.NewGuid()}")).StatusCode);
        AccountSummary pending = (await client.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{unconfirmed.Id}"))!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/admin/accounts/{pending.Id}/roles", new { roles = new[] { "Administrator" }, version = pending.Version })).StatusCode);
        await HttpLoginAsync(client, unconfirmed.Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/email-deliveries")).StatusCode);
    }

    [Fact]
    public async Task Roles_WhenAdministratorsRemoveThemselvesConcurrently_PreserveLastAdministratorAndRecheckExistingSessionsAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse first = await SeedAsync(factory, database, "adminone@example.test");
        AccountResponse second = await SeedAsync(factory, database, "admintwo@example.test");
        await AdminAsync(factory, admin => admin.BootstrapAsync(first.Email, CancellationToken.None));
        (AccountResponse firstProfile, Guid firstSession, _) = await LoginAsync(factory, first.Email);
        await AdminAsync(factory, admin => admin.ChangeRolesAsync(first.Id, firstSession, second.Id, ["Administrator"], 2, CancellationToken.None));
        (AccountResponse secondProfile, Guid secondSession, _) = await LoginAsync(factory, second.Email);
        using HttpClient firstCookie = Client(factory);
        using HttpClient secondCookie = Client(factory);
        await HttpLoginAsync(firstCookie, first.Email);
        await HttpLoginAsync(secondCookie, second.Email);
        async Task<int> RemoveAsync(AccountResponse account, Guid session)
        {
            try
            { await AdminAsync(factory, admin => admin.ChangeRolesAsync(account.Id, session, account.Id, [], account.Version, CancellationToken.None)); return 200; }
            catch (AccountOperationException exception) { Assert.Equal("last_administrator", exception.Code); return exception.Status; }
        }
        int[] results = await Task.WhenAll(RemoveAsync(firstProfile, firstSession), RemoveAsync(secondProfile, secondSession));
        Assert.Equal(1, results.Count(status => status == 200));
        Assert.Equal(1, results.Count(status => status == 409));
        Guid removed = results[0] == 200 ? first.Id : second.Id;
        Guid removedSession = results[0] == 200 ? firstSession : secondSession;
        AccountOperationException staleActor = await Assert.ThrowsAsync<AccountOperationException>(() => AdminAsync(factory, admin => admin.ChangeRolesAsync(removed, removedSession, first.Id, ["Author"], 3, CancellationToken.None)));
        Assert.Equal(403, staleActor.Status);
        AccountResponse current = (await AccountsAsync(factory, operations => operations.FindSessionAsync(removedSession, CancellationToken.None)))!;
        Assert.Equal(["RegularUser"], current.Roles);
        HttpClient removedCookie = results[0] == 200 ? firstCookie : secondCookie;
        Assert.Equal(HttpStatusCode.Forbidden, (await removedCookie.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await removedCookie.GetAsync("/api/account")).StatusCode);
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"AspNetUserRoles\" ur JOIN identity.\"AspNetRoles\" r ON r.\"Id\"=ur.\"RoleId\" WHERE r.\"Name\"='Administrator'"));
    }

    [Fact]
    public async Task Roles_WhenRevocationWins_RejectsAlreadyStartedWriteWithoutChangingTargetAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse first = await SeedAsync(factory, database, "winningadmin@example.test");
        AccountResponse second = await SeedAsync(factory, database, "revokedadmin@example.test");
        AccountResponse target = await SeedAsync(factory, database, "untouched@example.test");
        await AdminAsync(factory, admin => admin.BootstrapAsync(first.Email, CancellationToken.None));
        (_, Guid firstSession, _) = await LoginAsync(factory, first.Email);
        AccountSummary secondAdmin = await AdminAsync(factory, admin => admin.ChangeRolesAsync(first.Id, firstSession, second.Id, ["Administrator"], 2, CancellationToken.None));
        (_, Guid secondSession, _) = await LoginAsync(factory, second.Email);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var blocker = new NpgsqlConnection(database);
        await blocker.OpenAsync(timeout.Token);
        await using NpgsqlTransaction transaction = await blocker.BeginTransactionAsync(timeout.Token);
        await using var emailLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", blocker, transaction);
        emailLock.Parameters.AddWithValue("key", "email:" + first.Email.ToUpperInvariant());
        await emailLock.ExecuteNonQueryAsync(timeout.Token);
        Task<AccountSummary> revoke = AdminAsync(factory, admin => admin.ChangeRolesAsync(first.Id, firstSession, second.Id, [], secondAdmin.Version, timeout.Token));
        await using var observer = new NpgsqlConnection(database);
        await observer.OpenAsync(timeout.Token);

        async Task<int> WaitForBlockedBackendAsync(int blockerPid)
        {
            await using var query = new NpgsqlCommand("SELECT pid FROM pg_stat_activity WHERE @blocker = ANY(pg_blocking_pids(pid)) AND wait_event = 'advisory' LIMIT 1", observer);
            query.Parameters.AddWithValue("blocker", blockerPid);
            while (true)
            {
                object? result = await query.ExecuteScalarAsync(timeout.Token);
                if (result is int pid)
                    return pid;
                await Task.Yield();
            }
        }

        Task<AccountSummary> pending;
        try
        {
            // Revocation owns the administration lock while its actor's Email lock is held here.
            int revokerPid = await WaitForBlockedBackendAsync(blocker.ProcessID);
            pending = AdminAsync(factory, admin => admin.ChangeRolesAsync(second.Id, secondSession, target.Id, ["Author"], 2, timeout.Token));
            // Observe the second write inside PostgreSQL before allowing revocation to commit.
            await WaitForBlockedBackendAsync(revokerPid);
        }
        finally
        {
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(["RegularUser"], (await revoke).Roles);
        AccountOperationException denied = await Assert.ThrowsAsync<AccountOperationException>(() => pending);
        Assert.Equal(403, denied.Status);
        Assert.Equal("administrator_required", denied.Code);
        AccountSummary preserved = await AdminAsync(factory, admin => admin.GetAsync(first.Id, firstSession, target.Id, CancellationToken.None));
        Assert.Equal(2, preserved.Version);
        Assert.Equal(["RegularUser"], preserved.Roles);
        Assert.Empty((await AdminAsync(factory, admin => admin.HistoryAsync(first.Id, firstSession, target.Id, 1, 20, CancellationToken.None))).Items);
    }

    [Fact]
    public async Task RoleWrite_WhenAuditFails_RollsBackMembershipAndVersionAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse actor = await SeedAsync(factory, database, "auditadmin@example.test");
        AccountResponse target = await SeedAsync(factory, database, "audittarget@example.test");
        await AdminAsync(factory, admin => admin.BootstrapAsync(actor.Email, CancellationToken.None));
        (_, Guid session, _) = await LoginAsync(factory, actor.Email);
        await ExecuteAsync(database, "ALTER TABLE identity.\"RoleChanges\" ADD CONSTRAINT reject_audit CHECK (false) NOT VALID");
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => AdminAsync(factory, admin => admin.ChangeRolesAsync(actor.Id, session, target.Id, ["Editor"], 2, CancellationToken.None)));
        AccountSummary preserved = await AdminAsync(factory, admin => admin.GetAsync(actor.Id, session, target.Id, CancellationToken.None));
        Assert.Equal(2, preserved.Version);
        Assert.Equal(["RegularUser"], preserved.Roles);
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"RoleChanges\""));
    }

    [Fact]
    public async Task ReplayHttp_WithConcurrentRequests_QueuesSameLinkOnceAndAuditsWithoutExposingSecretsAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse actor = await SeedAsync(factory, database, "mailadmin@example.test");
        AccountResponse target = await SeedAsync(factory, database, "mailtarget@example.test", false);
        await AdminAsync(factory, admin => admin.BootstrapAsync(actor.Email, CancellationToken.None));
        using HttpClient client = Client(factory);
        await HttpLoginAsync(client, actor.Email);
        await ExecuteAsync(database, "UPDATE notifications.\"EmailDeliveries\" SET \"State\"='Failed', \"AttemptCount\"=6, \"RetryCount\"=6 WHERE \"AccountId\"='" + target.Id + "'");
        CityPortal.Notifications.EmailDeliveryPage list = (await client.GetFromJsonAsync<CityPortal.Notifications.EmailDeliveryPage>("/api/admin/email-deliveries?state=Failed"))!;
        CityPortal.Notifications.EmailDeliverySummary delivery = Assert.Single(list.Items);
        string metadata = await client.GetStringAsync("/api/admin/email-deliveries");
        Assert.DoesNotContain("token", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedPayload", metadata);
        Assert.DoesNotContain(target.Email, metadata);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/email-deliveries/{delivery.Id}/replay", new { version = delivery.Version })).StatusCode);
        await CsrfAsync(client);
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync($"/api/admin/email-deliveries/{delivery.Id}/replay", new { version = delivery.Version })));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (HttpResponseMessage response in responses)
            response.Dispose();
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM notifications.\"Replays\""));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM notifications.\"Replays\" WHERE \"ActorId\"='" + actor.Id + "'"));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"Id\"='" + delivery.Id + "' AND \"AttemptCount\"=6 AND \"RetryCount\"=0 AND \"State\"='Queued' AND \"Version\"=2"));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM identity.\"AspNetUsers\" WHERE \"Id\"='" + target.Id + "' AND \"LatestConfirmationMessageId\"='" + delivery.Id + "'"));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("invalidated")]
    [InlineData("terminal")]
    [InlineData("audit_failure")]
    public async Task Replay_WhenInvalidOrAuditFails_DoesNotQueueOrRecordPartialChangesAsync(string condition)
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        AccountResponse actor = await SeedAsync(factory, database, "failureadmin@example.test");
        AccountResponse target = await SeedAsync(factory, database, "failuretarget@example.test", false);
        await AdminAsync(factory, admin => admin.BootstrapAsync(actor.Email, CancellationToken.None));
        (_, Guid session, _) = await LoginAsync(factory, actor.Email);
        await ExecuteAsync(database, "UPDATE notifications.\"EmailDeliveries\" SET \"State\"='Failed' WHERE \"AccountId\"='" + target.Id + "'");
        CityPortal.Notifications.EmailDeliverySummary delivery = Assert.Single((await AdminAsync(factory, admin => admin.DeliveriesAsync(actor.Id, session, "Failed", 1, 20, CancellationToken.None))).Items);
        if (condition == "expired")
            await ExecuteAsync(database, "UPDATE notifications.\"EmailDeliveries\" SET \"ExpiresAtUtc\"=now()-interval '1 second' WHERE \"Id\"='" + delivery.Id + "'");
        if (condition == "invalidated")
            await ExecuteAsync(database, "UPDATE identity.\"Links\" SET \"State\"='Invalidated' WHERE \"AccountId\"='" + target.Id + "'");
        if (condition == "terminal")
            await ExecuteAsync(database, "UPDATE notifications.\"EmailDeliveries\" SET \"State\"='Sent' WHERE \"Id\"='" + delivery.Id + "'");
        if (condition == "audit_failure")
        {
            await ExecuteAsync(database, "ALTER TABLE notifications.\"Replays\" ADD CONSTRAINT reject_replay CHECK(false) NOT VALID");
            await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => AdminAsync(factory, admin => admin.ReplayAsync(actor.Id, session, delivery.Id, delivery.Version, CancellationToken.None)));
        }
        else
        {
            AccountOperationException rejected = await Assert.ThrowsAsync<AccountOperationException>(() => AdminAsync(factory, admin => admin.ReplayAsync(actor.Id, session, delivery.Id, delivery.Version, CancellationToken.None)));
            Assert.Equal(condition == "terminal" ? 409 : 400, rejected.Status);
        }
        Assert.Equal(0L, await ScalarAsync(database, "SELECT count(*) FROM notifications.\"Replays\""));
        Assert.Equal(1L, await ScalarAsync(database, "SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"Id\"='" + delivery.Id + "' AND \"Version\"=1"));
    }

    private static async Task<AccountResponse> SeedAsync(ApiFactory factory, string database, string email, bool confirmed = true)
    {
        AccountResponse account = await AccountsAsync(factory, operations => operations.RegisterAsync(email, "Resident", _password, CancellationToken.None));
        if (confirmed)
            await ConfirmAsync(factory, database, account.Id);
        return account;
    }

    private static async Task ConfirmAsync(ApiFactory factory, string database, Guid id)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT \"LinkId\", \"ProtectedPayload\" FROM notifications.\"EmailDeliveries\" WHERE \"AccountId\"=@id", connection);
        query.Parameters.AddWithValue("id", id);
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Guid link = reader.GetGuid(0);
        using var payload = JsonDocument.Parse(factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("CityPortal.AccountMail.v1").Unprotect(reader.GetString(1)));
        await AccountsAsync(factory, operations => operations.ConfirmAsync(link, payload.RootElement.GetProperty("Token").GetString(), CancellationToken.None));
    }

    private static Task<(AccountResponse Account, Guid SessionId, DateTimeOffset ExpiresAtUtc)> LoginAsync(ApiFactory factory, string email)
        => AccountsAsync(factory, operations => operations.LoginAsync(email, _password, false, CancellationToken.None));

    private static async Task<T> AccountsAsync<T>(ApiFactory factory, Func<AccountOperations, Task<T>> action)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccountOperations>());
    }

    private static async Task<T> AdminAsync<T>(ApiFactory factory, Func<AccountAdministration, Task<T>> action)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccountAdministration>());
    }

    private static async Task AdminAsync(ApiFactory factory, Func<AccountAdministration, Task> action)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AccountAdministration>());
    }

    private static HttpClient Client(ApiFactory factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task<int> BootstrapCommandAsync(ApiFactory factory, string database, string email)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(CityPortal.Api.Program).Assembly.Location);
        start.ArgumentList.Add("--bootstrap-admin");
        start.ArgumentList.Add(email);
        start.Environment["ConnectionStrings__Database"] = database;
        start.Environment["ConnectionStrings__Messaging"] = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Messaging");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Bootstrap process did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.DoesNotContain(_password, await output);
        Assert.DoesNotContain(_password, await error);
        return process.ExitCode;
    }

    private static async Task HttpLoginAsync(HttpClient client, string email)
    {
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = _password, rememberMe = false })).StatusCode);
    }

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

    private static async Task<long> ScalarAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
