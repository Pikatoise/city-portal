using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

[Collection("Media upload slot")]
public sealed partial class NewsTests : IClassFixture<MediaFixture>
{
    private readonly MediaFixture _fixture;
    private const string _password = "NewsPassword1!";

    public NewsTests(MediaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Drafts_IncompleteSaveReopenAndCompetingVersions_PreserveOneCommittedRevisionAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using (owner)
        {
            ArticleResponse article = await CreateAsync(owner);
            Assert.Equal("", article.Content.Title);
            Assert.Null(article.Content.CategoryId);
            ArticleContent content = NewsDocumentTests.Content("<script>private</script>") with { Summary = "Saved without title or category" };
            article = await SaveAsync(owner, article, content);
            ArticleResponse reopened = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal(content.Summary, reopened.Content.Summary);
            Assert.Contains("&lt;script&gt;", reopened.PreviewHtml);
            Assert.DoesNotContain("<script>", reopened.PreviewHtml);
            Assert.True(reopened.Version > 1);
            Task<HttpResponseMessage>[] changes = new[] { "First tab", "Second tab" }.Select(title => owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content = content with { Title = title } })).ToArray();
            HttpResponseMessage[] results = await Task.WhenAll(changes);
            Assert.Equal(1, results.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            ArticleResponse winner = (await results.Single(response => response.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<ArticleResponse>())!;
            Assert.Equal(winner.Content.Title, (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!.Content.Title);
            Assert.Equal(article.RevisionId, winner.RevisionId);
            ArticleContent invalid = content with { Body = JsonSerializer.SerializeToElement(new { type = "doc", attrs = new { onclick = "unsafe" } }) };
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { winner.Version, content = invalid })).StatusCode);
            Assert.Equal(winner.Version, (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!.Version);
            Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"Revisions\" WHERE \"ArticleId\"=@id", article.Id));
            foreach (HttpResponseMessage result in results)
                result.Dispose();
            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { winner.Version, content })).StatusCode);
        }
    }

    [Fact]
    public async Task Drafts_PrivateOwnersAndRoleRemoval_DenyOtherAuthorsAndStaffAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid authorId, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient other) = await ActorAsync(factory, database, "Author");
        (_, HttpClient administrator) = await ActorAsync(factory, database, "Author", "Administrator");
        using (owner)
        using (other)
        using (administrator)
        using (HttpClient anonymous = Client(factory))
        {
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), NewsDocumentTests.Content("Private draft") with { Title = "Secret" });
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/editorial/articles/{article.Id}")).StatusCode);
            foreach (HttpClient outsider in new[] { other, administrator })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/editorial/articles/{article.Id}")).StatusCode);
                Assert.Empty((await outsider.GetFromJsonAsync<NewsPage<ArticleSummaryResponse>>("/api/editorial/articles"))!.Items);
                Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync($"/api/editorial/articles/{article.Id}?version={article.Version}")).StatusCode);
            }
            await ExecuteAsync(database, "DELETE FROM identity.\"AspNetUserRoles\" WHERE \"UserId\"=@id AND \"RoleId\" IN (SELECT \"Id\" FROM identity.\"AspNetRoles\" WHERE \"Name\"='Author')", authorId);
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/editorial/articles/{article.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/editorial/articles")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, article.Content })).StatusCode);
        }
    }

    [Fact]
    public async Task Taxonomy_UniqueArchivedNamesAndExistingAssociations_EnforcePermissionsAndVersionsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid editorId, HttpClient editor) = await ActorAsync(factory, database, "Administrator");
        using (owner)
        using (editor)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/editorial/categories", new { name = "Town" })).StatusCode);
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Town");
            TaxonomyResponse tag = await TaxonomyAsync(editor, "tags", "Town");
            Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync("/api/editorial/categories", new { name = " town " })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/editorial/categories", new { name = new string('a', 101) })).StatusCode);
            ArticleResponse existing = await CreateAsync(owner);
            ArticleContent content = NewsDocumentTests.Content("Article") with { CategoryId = category.Id, TagIds = [tag.Id] };
            existing = await SaveAsync(owner, existing, content);
            foreach (TaxonomyResponse entry in new[] { category, tag })
            {
                Assert.Equal(HttpStatusCode.OK, (await editor.PatchAsJsonAsync($"/api/editorial/{entry.Kind}/{entry.Id}", new { entry.Version, entry.Name, archived = true })).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await editor.PatchAsJsonAsync($"/api/editorial/{entry.Kind}/{entry.Id}", new { entry.Version, name = "Stale rename", archived = true })).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/editorial/{entry.Kind}", new { name = " TOWN " })).StatusCode);
            }
            existing = await SaveAsync(owner, existing, content with { Title = "Still associated" });
            Assert.Equal(category.Id, existing.Content.CategoryId);
            Assert.Equal(tag.Id, Assert.Single(existing.Content.TagIds));
            ArticleResponse fresh = await CreateAsync(owner);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{fresh.Id}", new { fresh.Version, content })).StatusCode);
            NewsPage<TaxonomyResponse> categories = (await owner.GetFromJsonAsync<NewsPage<TaxonomyResponse>>("/api/editorial/categories"))!;
            Assert.True(Assert.Single(categories.Items).Archived);
            Assert.Equal(2L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"TaxonomyChanges\" WHERE \"TaxonomyId\"=@id", category.Id));
            Assert.Equal(editorId, await ScalarAsync<Guid>(database, "SELECT \"ActorId\" FROM news.\"TaxonomyChanges\" WHERE \"TaxonomyId\"=@id ORDER BY \"Version\" DESC LIMIT 1", category.Id));
        }
    }

    [Fact]
    public async Task Drafts_MediaSelectionAndRetainedDeletion_CommitReferencesAtomicallyWithoutPublicAccessAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid ownerId, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient other) = await ActorAsync(factory, database, "Author", "Moderator");
        using (owner)
        using (other)
        using (HttpClient anonymous = Client(factory))
        {
            ImageResponse image = await UploadAsync(owner);
            ArticleResponse article = await CreateAsync(owner);
            ArticleContent content = NewsDocumentTests.Content("Retain this complete text") with { Title = "Saved image", Cover = new ArticleImage(image.Id, "River") };
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content })).StatusCode);
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
            article = await SaveAsync(owner, article, content);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            ArticleResponse foreign = await CreateAsync(other);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsJsonAsync($"/api/editorial/articles/{foreign.Id}", new { foreign.Version, content })).StatusCode);
            Assert.Equal(foreign.Version, (await other.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{foreign.Id}"))!.Version);
            using HttpResponseMessage proposed = await owner.PostAsJsonAsync("/api/media/templates", new { imageId = image.Id, name = "Shared river" });
            TemplateResponse template = (await proposed.Content.ReadFromJsonAsync<TemplateResponse>())!;
            using HttpResponseMessage submitted = await owner.PostAsJsonAsync($"/api/media/templates/{template.Id}/submit", new { template.Version });
            template = (await submitted.Content.ReadFromJsonAsync<TemplateResponse>())!;
            Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync($"/api/media/templates/{template.Id}/approve", new { template.Version })).StatusCode);
            foreign = await SaveAsync(other, foreign, content);
            Assert.Equal(image.Id, foreign.Content.Cover?.MediaId);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/editorial/articles/{article.Id}?version={article.Version}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/editorial/articles/{article.Id}")).StatusCode);
            Assert.Empty((await owner.GetFromJsonAsync<NewsPage<ArticleSummaryResponse>>("/api/editorial/articles"))!.Items);
            string retained = await ScalarAsync<string>(database, "SELECT \"ContentJson\"::text FROM news.\"Revisions\" WHERE \"Id\"=@id AND \"Retained\"", article.RevisionId);
            Assert.Contains("Retain this complete text", retained);
            Assert.False(await ScalarAsync<bool>(database, "SELECT \"Current\" OR \"Submitted\" OR \"Public\" FROM media.\"References\" WHERE \"RevisionId\"=@id", article.RevisionId));
            Assert.Equal(ownerId, await ScalarAsync<Guid>(database, "SELECT \"AuthorId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
        }
    }

    private static HttpClient Client(ApiFactory factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task CsrfAsync(HttpClient client)
    {
        JsonElement token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }

    private static async Task<(Guid Id, HttpClient Client)> ActorAsync(ApiFactory factory, string database, params string[] roles)
    {
        string email = Guid.NewGuid().ToString("N") + "@news.test";
        AccountResponse account;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            account = await scope.ServiceProvider.GetRequiredService<AccountOperations>().RegisterAsync(email, "NewsAuthor", _password, CancellationToken.None);
        await ExecuteAsync(database, "UPDATE identity.\"AspNetUsers\" SET \"EmailConfirmed\"=true WHERE \"Id\"=@id", account.Id);
        foreach (string role in roles)
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("INSERT INTO identity.\"AspNetRoles\" (\"Id\", \"Name\", \"NormalizedName\") VALUES (@roleId, @role, upper(@role)) ON CONFLICT (\"NormalizedName\") DO NOTHING; INSERT INTO identity.\"AspNetUserRoles\" (\"UserId\", \"RoleId\") SELECT @id, \"Id\" FROM identity.\"AspNetRoles\" WHERE \"Name\"=@role ON CONFLICT DO NOTHING;", connection);
            command.Parameters.AddWithValue("id", account.Id);
            command.Parameters.AddWithValue("roleId", Guid.NewGuid());
            command.Parameters.AddWithValue("role", role);
            await command.ExecuteNonQueryAsync();
        }
        HttpClient client = Client(factory);
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = _password, rememberMe = false })).StatusCode);
        await CsrfAsync(client);
        return (account.Id, client);
    }

    private static async Task<ArticleResponse> CreateAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/editorial/articles", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArticleResponse>())!;
    }

    private static async Task<ArticleResponse> SaveAsync(HttpClient client, ArticleResponse article, ArticleContent content)
    {
        using HttpResponseMessage response = await client.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArticleResponse>())!;
    }

    private static async Task<TaxonomyResponse> TaxonomyAsync(HttpClient client, string kind, string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/editorial/{kind}", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaxonomyResponse>())!;
    }

    private static async Task<ImageResponse> UploadAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(MediaProcessorTests.Image());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "river.png");
        using HttpResponseMessage response = await client.PostAsync("/api/media/images", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImageResponse>())!;
    }

    private static async Task<T> ScalarAsync<T>(string database, string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string database, string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
}
