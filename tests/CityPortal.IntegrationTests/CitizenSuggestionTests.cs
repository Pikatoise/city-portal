using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.CitizenSubmissions;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class CitizenSuggestionTests : IClassFixture<RegistrationFixture>
{
    private const string _path = "/api/account/suggestions";
    private const string _staff = "/api/staff/suggestions";
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
        using HttpClient author = await LoginAsync(factory, database, "author", confirmed: true, role: "Author");
        using HttpClient moderator = await LoginAsync(factory, database, "moderator", confirmed: true, role: "Moderator");
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
        Assert.Empty((await QueueAsync(author)).Items);
        Assert.Empty((await moderator.GetFromJsonAsync<StaffSuggestionPage>($"{_staff}/moderation"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await author.PostAsJsonAsync($"{_staff}/{suggestion.Id}/claim", new { saved.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.PostAsJsonAsync($"{_staff}/{suggestion.Id}/delete",
            new { saved.Version, reason = "Expired suggestion" })).StatusCode);
        await using var beforeCleanup = new NpgsqlCommand("SELECT count(*) FROM citizen_submissions.\"Suggestions\"", connection);
        Assert.Equal(1L, await beforeCleanup.ExecuteScalarAsync());

        await using AsyncServiceScope cleanup = factory.Services.CreateAsyncScope();
        // The job uses the application time and cancellation token only.
        await cleanup.ServiceProvider.GetRequiredService<SuggestionExpiryJob>().Execute(null!, CancellationToken.None);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM citizen_submissions.\"Suggestions\"", connection);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task StaffSuggestions_ProtectContactsAndPreserveDeletionAuditAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        await factory.Services.MigrateMediaAsync(CancellationToken.None);
        using HttpClient resident = await LoginAsync(factory, database, "resident", confirmed: true);
        using HttpClient first = await LoginAsync(factory, database, "authorone", confirmed: true, role: "Author");
        using HttpClient second = await LoginAsync(factory, database, "authortwo", confirmed: true, role: "Author");
        using HttpClient moderator = await LoginAsync(factory, database, "moderator", confirmed: true, role: "Moderator");
        using HttpClient administrator = await LoginAsync(factory, database, "admin", confirmed: true, role: "Administrator");
        var input = new SuggestionInput { Title = "Private topic", Description = "Unpublished details.", Contact = "contact@example.test" };
        using HttpResponseMessage submitted = await resident.PostAsJsonAsync(_path, input);
        submitted.EnsureSuccessStatusCode();
        SuggestionResponse item = (await submitted.Content.ReadFromJsonAsync<SuggestionResponse>())!;
        string claimPath = $"{_staff}/{item.Id}/claim";
        string returnPath = $"{_staff}/{item.Id}/return";
        string deletePath = $"{_staff}/{item.Id}/delete";
        Assert.Null(Assert.Single((await QueueAsync(first)).Items).Contact);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync($"{_staff}/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync($"{_staff}/moderation")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync(claimPath, new { version = 0 })).StatusCode);
        HttpResponseMessage[] attempts = await Task.WhenAll(first.PostAsJsonAsync(claimPath, new { item.Version }),
            second.PostAsJsonAsync(claimPath, new { item.Version }));
        using HttpResponseMessage firstClaim = attempts[0];
        using HttpResponseMessage secondClaim = attempts[1];
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        HttpClient winner = firstClaim.StatusCode == HttpStatusCode.NoContent ? first : second;
        HttpClient other = ReferenceEquals(winner, first) ? second : first;
        Assert.Empty((await QueueAsync(other)).Items);
        Assert.Empty((await QueueAsync(other, mine: true)).Items);
        StaffSuggestionResponse assigned = Assert.Single((await QueueAsync(winner, mine: true)).Items);
        Assert.Equal(input.Contact, assigned.Contact);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(returnPath, new { assigned.Version })).StatusCode);
        StaffSuggestionResponse moderation = Assert.Single((await moderator.GetFromJsonAsync<StaffSuggestionPage>($"{_staff}/moderation"))!.Items);
        Assert.True(moderation.Assigned);
        Assert.Null(moderation.Contact);
        Assert.Equal(HttpStatusCode.Forbidden, (await winner.PostAsJsonAsync(deletePath, new { assigned.Version, reason = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await winner.PostAsJsonAsync(returnPath, new { assigned.Version })).StatusCode);
        StaffSuggestionResponse returned = Assert.Single((await QueueAsync(other)).Items);
        Assert.False((await resident.GetFromJsonAsync<SuggestionResponse>($"{_path}/{item.Id}"))!.CanEdit);
        Assert.Equal(HttpStatusCode.NoContent, (await winner.PostAsJsonAsync(claimPath, new { returned.Version })).StatusCode);
        using HttpResponseMessage secondSubmission = await resident.PostAsJsonAsync(_path, input with { Title = "Second topic" });
        secondSubmission.EnsureSuccessStatusCode();
        SuggestionResponse secondItem = (await secondSubmission.Content.ReadFromJsonAsync<SuggestionResponse>())!;

        AccountResponse author = (await winner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        string accountPath = $"/api/admin/accounts/{author.Id}";
        AccountSummary account = (await administrator.GetFromJsonAsync<AccountSummary>(accountPath))!;
        HttpResponseMessage[] roleRace = await Task.WhenAll(winner.PostAsJsonAsync($"{_staff}/{secondItem.Id}/claim", new { secondItem.Version }),
            administrator.PutAsJsonAsync(accountPath + "/roles", new { roles = Array.Empty<string>(), account.Version }));
        using HttpResponseMessage reclaim = roleRace[0];
        using HttpResponseMessage revoke = roleRace[1];
        revoke.EnsureSuccessStatusCode();
        Assert.Contains(reclaim.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Forbidden });
        Assert.Equal(HttpStatusCode.Forbidden, (await winner.GetAsync($"{_staff}/queue?mine=true")).StatusCode);
        StaffSuggestionResponse[] available = (await QueueAsync(other)).Items;
        Assert.Equal(2, available.Length);
        StaffSuggestionResponse released = Assert.Single(available, entry => entry.Id == item.Id);
        StaffSuggestionResponse secondAvailable = Assert.Single(available, entry => entry.Id == secondItem.Id);
        Assert.Equal(returned.Version + 2, released.Version);
        Assert.Equal(secondItem.Version + (reclaim.StatusCode == HttpStatusCode.NoContent ? 2 : 0), secondAvailable.Version);
        Assert.False((await resident.GetFromJsonAsync<SuggestionResponse>($"{_path}/{item.Id}"))!.CanEdit);
        Assert.Equal(HttpStatusCode.BadRequest, (await moderator.PostAsJsonAsync(deletePath, new { released.Version, reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await moderator.PostAsJsonAsync(deletePath, new { item.Version, reason = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync(deletePath, new { released.Version, reason = " Duplicate " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync($"{_path}/{item.Id}")).StatusCode);
        Assert.Equal(secondItem.Id, Assert.Single((await QueueAsync(other)).Items).Id);

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        AccountResponse actingModerator = (await moderator.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        await using var command = new NpgsqlCommand("SELECT \"ActorId\", \"Reason\", NOT EXISTS (SELECT 1 FROM citizen_submissions.\"Suggestions\" WHERE \"Id\" = @id) FROM citizen_submissions.\"Deletions\" WHERE \"SuggestionId\" = @id", connection);
        command.Parameters.AddWithValue("id", item.Id);
        await using NpgsqlDataReader audit = await command.ExecuteReaderAsync();
        Assert.True(await audit.ReadAsync());
        Assert.Equal(actingModerator.Id, audit.GetGuid(0));
        Assert.Equal("Duplicate", audit.GetString(1));
        Assert.True(audit.GetBoolean(2));
        Assert.False(await audit.ReadAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.PostAsJsonAsync($"{_staff}/{secondItem.Id}/delete",
            new { secondAvailable.Version, reason = "Duplicate" })).StatusCode);
        Assert.Empty((await QueueAsync(other)).Items);
    }

    [Fact]
    public async Task AssignedSuggestion_PausesExpiryAndSurvivesPublicationAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        await factory.Services.MigrateMediaAsync(CancellationToken.None);
        using HttpClient resident = await LoginAsync(factory, database, "Resident", confirmed: true);
        using HttpClient author = await LoginAsync(factory, database, "Author", confirmed: true, role: "Author");
        using HttpClient editor = await LoginAsync(factory, database, "Editor", confirmed: true, role: "Editor");
        using HttpClient moderator = await LoginAsync(factory, database, "Moderator", confirmed: true, role: "Moderator");
        using HttpClient publicClient = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        const string contact = "private@example.test";
        var input = new SuggestionInput { Title = "River cleanup", Description = "First line\nSecond line", Contact = contact };
        SuggestionResponse created = await ReadAsync<SuggestionResponse>(await resident.PostAsJsonAsync(_path, input));
        string residentPath = $"{_path}/{created.Id}";
        string claimPath = $"{_staff}/{created.Id}/claim";
        string returnPath = $"{_staff}/{created.Id}/return";
        Assert.Equal("Queued", created.Status);
        SuggestionResponse persisted = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;

        time.Advance(TimeSpan.FromHours(20));
        using HttpResponseMessage firstClaim = await author.PostAsJsonAsync(claimPath, new { created.Version });
        firstClaim.EnsureSuccessStatusCode();
        StaffSuggestionResponse first = Assert.Single((await QueueAsync(author, mine: true)).Items);
        ArticleResponse firstDraft = (await author.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{first.ArticleId}"))!;
        Assert.Equal(input.Title, firstDraft.Content.Title);
        Assert.Contains("First line<br>Second line", firstDraft.PreviewHtml);
        Assert.DoesNotContain(contact, firstDraft.Content.Body.GetRawText());
        Assert.Null(first.ExpiresAtUtc);

        time.Advance(TimeSpan.FromHours(80));
        await ExpireAsync(factory);
        SuggestionResponse assigned = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;
        Assert.Equal("InProgress", assigned.Status);
        Assert.Null(assigned.ExpiresAtUtc);
        Assert.Null(assigned.PublicArticleUrl);
        using HttpResponseMessage returned = await author.PostAsJsonAsync(returnPath, new { first.Version });
        returned.EnsureSuccessStatusCode();
        using HttpResponseMessage deleted = await author.GetAsync($"/api/editorial/articles/{firstDraft.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        SuggestionResponse queued = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;
        Assert.Equal("Queued", queued.Status);
        Assert.False(queued.CanEdit);
        Assert.Equal(persisted.ExpiresAtUtc?.AddHours(80), queued.ExpiresAtUtc);

        time.Advance(TimeSpan.FromHours(30));
        using HttpResponseMessage secondClaim = await author.PostAsJsonAsync(claimPath, new { queued.Version });
        secondClaim.EnsureSuccessStatusCode();
        StaffSuggestionResponse second = Assert.Single((await QueueAsync(author, mine: true)).Items);
        ArticleResponse article = (await author.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{second.ArticleId}"))!;
        Assert.NotEqual(firstDraft.Id, article.Id);
        TaxonomyResponse category = await ReadAsync<TaxonomyResponse>(await editor.PostAsJsonAsync("/api/editorial/categories", new { name = "Community" }));
        ArticleContent complete = article.Content with { Summary = "Volunteers clean the river.", CategoryId = category.Id };
        article = await ReadAsync<ArticleResponse>(await author.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content = complete }));
        article = await ActAsync(author, article, "submit");
        article = await ActAsync(editor, article, "claim");
        article = await ActAsync(editor, article, "approve");
        article = await ActAsync(author, article, "publish");

        time.Advance(TimeSpan.FromDays(8));
        await ExpireAsync(factory);
        SuggestionResponse published = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;
        Assert.Equal("Published", published.Status);
        Assert.Null(published.ExpiresAtUtc);
        Assert.Equal(article.PublicAddress, published.PublicArticleUrl);
        string url = Assert.IsType<string>(published.PublicArticleUrl);
        PublicArticleResponse publicArticle = (await publicClient.GetFromJsonAsync<PublicArticleResponse>("/api/news/articles/by-address/" + url["/news/".Length..]))!;
        Assert.Equal("Author", publicArticle.AuthorDisplayName);
        Assert.Equal("Resident", publicArticle.IdeaContributorDisplayName);
        Assert.DoesNotContain(contact, publicArticle.Content.Body.GetRawText());
        using HttpResponseMessage cannotReturn = await author.PostAsJsonAsync(returnPath, new { second.Version });
        Assert.Equal(HttpStatusCode.Conflict, cannotReturn.StatusCode);
        StaffSuggestionResponse moderation = Assert.Single((await moderator.GetFromJsonAsync<StaffSuggestionPage>($"{_staff}/moderation"))!.Items);
        Assert.True(moderation.Published);
        Assert.Null(moderation.ArticleId);
        using HttpResponseMessage cannotDelete = await moderator.PostAsJsonAsync($"{_staff}/{created.Id}/delete",
            new { second.Version, reason = "Remove published suggestion" });
        Assert.Equal(HttpStatusCode.Conflict, cannotDelete.StatusCode);

        await ActAsync(editor, article, "withdraw", "Correct a factual error.");
        SuggestionResponse withdrawn = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;
        Assert.Equal("Published", withdrawn.Status);
        Assert.Null(withdrawn.PublicArticleUrl);
    }

    private static async Task ExpireAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        // The job uses the application time and cancellation token only.
        await scope.ServiceProvider.GetRequiredService<SuggestionExpiryJob>().Execute(null!, CancellationToken.None);
    }

    private static Task<ArticleResponse> ActAsync(HttpClient client, ArticleResponse article, string action, string? reason = null) =>
        ActAsync(client, $"/api/editorial/articles/{article.Id}/{action}", article.Version, reason);

    private static async Task<ArticleResponse> ActAsync(HttpClient client, string path, long version, string? reason) =>
        await ReadAsync<ArticleResponse>(await client.PostAsJsonAsync(path, new { version, reason }));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) where T : class
    {
        using (response)
        {
            response.EnsureSuccessStatusCode();
            return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>());
        }
    }

    private static async Task<StaffSuggestionPage> QueueAsync(HttpClient client, bool mine = false) =>
        (await client.GetFromJsonAsync<StaffSuggestionPage>($"{_staff}/queue?mine={mine}"))!;

    private static async Task<HttpClient> LoginAsync(ApiFactory factory, string database, string name, bool confirmed, string? role = null)
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
            if (role is not null)
            {
                await using var add = new NpgsqlCommand("INSERT INTO identity.\"AspNetRoles\" (\"Id\", \"Name\", \"NormalizedName\") VALUES (@roleId, @role, upper(@role)) ON CONFLICT (\"NormalizedName\") DO NOTHING; INSERT INTO identity.\"AspNetUserRoles\" (\"UserId\", \"RoleId\") SELECT @id, \"Id\" FROM identity.\"AspNetRoles\" WHERE \"Name\" = @role", connection);
                add.Parameters.AddWithValue("id", account.Id);
                add.Parameters.AddWithValue("roleId", Guid.NewGuid());
                add.Parameters.AddWithValue("role", role);
                await add.ExecuteNonQueryAsync();
            }
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
