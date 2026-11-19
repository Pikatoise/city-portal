using System.Net;
using System.Net.Http.Json;
using CityPortal.Identity;
using CityPortal.News;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Fact]
    public async Task DraftSave_WhenAnotherAuthorWaitsForTheirDraft_CommitsIndependentlyAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient first) = await ActorAsync(factory, database, "Author");
        using HttpClient firstAuthor = first;
        (_, HttpClient second) = await ActorAsync(factory, database, "Author");
        using HttpClient secondAuthor = second;
        ArticleResponse firstDraft = await CreateAsync(firstAuthor);
        ArticleResponse secondDraft = await CreateAsync(secondAuthor);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var blocker = new NpgsqlConnection(database);
        await blocker.OpenAsync(timeout.Token);
        await using var observer = new NpgsqlConnection(database);
        await observer.OpenAsync(timeout.Token);
        await using NpgsqlTransaction transaction = await blocker.BeginTransactionAsync(timeout.Token);
        await LockDraftAsync(blocker, transaction, firstDraft.Id, timeout.Token);
        Task<HttpResponseMessage> held = SaveDraftResponseAsync(firstAuthor, firstDraft, "First saved draft", timeout.Token);
        int? firstBackend = await WaitForWriteBlockAsync(observer, blocker.ProcessID, held, timeout.Token);
        Assert.NotNull(firstBackend);

        Task<HttpResponseMessage> independent = SaveDraftResponseAsync(secondAuthor, secondDraft, "Second saved draft", timeout.Token);
        int? blockedSecondBackend = await WaitForWriteBlockAsync(observer, firstBackend.Value, independent, timeout.Token);
        if (blockedSecondBackend is null)
            Assert.False(held.IsCompleted);
        await transaction.CommitAsync(timeout.Token);

        using HttpResponseMessage firstResult = await held;
        using HttpResponseMessage secondResult = await independent;
        Assert.Null(blockedSecondBackend);
        Assert.Equal(HttpStatusCode.OK, firstResult.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResult.StatusCode);
        ArticleResponse? secondSaved = await secondResult.Content.ReadFromJsonAsync<ArticleResponse>(timeout.Token);
        Assert.Equal("Second saved draft", secondSaved?.Content.Title);
        Assert.Equal(secondDraft.Version + 1, secondSaved?.Version);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DraftSave_WhenAuthorRoleIsRevoked_FollowsTheCommitOrderAsync(bool revocationFirst)
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid authorId, HttpClient authorClient) = await ActorAsync(factory, database, "Author");
        using HttpClient author = authorClient;
        (_, HttpClient adminClient) = await ActorAsync(factory, database, "Administrator");
        using HttpClient administrator = adminClient;
        ArticleResponse draft = await CreateAsync(author);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        AccountResponse? admin = await administrator.GetFromJsonAsync<AccountResponse>("/api/account", timeout.Token);
        AccountResponse? account = await author.GetFromJsonAsync<AccountResponse>("/api/account", timeout.Token);
        Assert.NotNull(admin);
        Assert.NotNull(account);
        await using var blocker = new NpgsqlConnection(database);
        await blocker.OpenAsync(timeout.Token);
        await using var observer = new NpgsqlConnection(database);
        await observer.OpenAsync(timeout.Token);
        await using NpgsqlTransaction transaction = await blocker.BeginTransactionAsync(timeout.Token);
        if (revocationFirst)
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", blocker, transaction);
            command.Parameters.AddWithValue("key", "email:" + admin.Email.ToUpperInvariant());
            await command.ExecuteNonQueryAsync(timeout.Token);
        }
        else
            await LockDraftAsync(blocker, transaction, draft.Id, timeout.Token);

        Task<HttpResponseMessage> leading = revocationFirst ? RevokeAsync() : SaveAsync();
        int? leadingBackend = await WaitForWriteBlockAsync(observer, blocker.ProcessID, leading, timeout.Token);
        Assert.NotNull(leadingBackend);
        Task<HttpResponseMessage> following = revocationFirst ? SaveAsync() : RevokeAsync();
        int? followingBackend = await WaitForWriteBlockAsync(observer, leadingBackend.Value, following, timeout.Token);
        await transaction.CommitAsync(timeout.Token);

        using HttpResponseMessage first = await leading;
        using HttpResponseMessage second = await following;
        Assert.NotNull(followingBackend);
        HttpResponseMessage revoke = revocationFirst ? first : second;
        HttpResponseMessage save = revocationFirst ? second : first;
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(revocationFirst ? HttpStatusCode.Forbidden : HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(draft.Version + (revocationFirst ? 0 : 1),
            await ScalarAsync<long>(database, """SELECT "Version" FROM news."Articles" WHERE "Id"=@id""", draft.Id));
        using HttpResponseMessage denied = await author.GetAsync($"/api/editorial/articles/{draft.Id}", timeout.Token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        Task<HttpResponseMessage> RevokeAsync() =>
            administrator.PutAsJsonAsync($"/api/admin/accounts/{authorId}/roles", new { roles = Array.Empty<string>(), account.Version }, timeout.Token);

        Task<HttpResponseMessage> SaveAsync() => SaveDraftResponseAsync(author, draft, "Saved before revocation", timeout.Token);
    }

    private static Task<HttpResponseMessage> SaveDraftResponseAsync(HttpClient client, ArticleResponse draft, string title, CancellationToken cancellationToken) =>
        client.PatchAsJsonAsync($"/api/editorial/articles/{draft.Id}",
            new { draft.Version, content = NewsDocumentTests.Content(title) with { Title = title } }, cancellationToken);

    private static async Task LockDraftAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""SELECT "Id" FROM news."Articles" WHERE "Id"=@id FOR UPDATE""", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<int?> WaitForWriteBlockAsync(NpgsqlConnection observer, int blocker, Task request, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND @blocker = ANY(pg_blocking_pids(pid)) LIMIT 1", observer);
        command.Parameters.AddWithValue("blocker", blocker);
        while (!request.IsCompleted)
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is int pid)
                return pid;
            await Task.Delay(10, cancellationToken);
        }
        return null;
    }
}
