using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Theory]
    [InlineData("Draft")]
    [InlineData("InReview")]
    [InlineData("Approved")]
    public async Task Published_DirectCorrectionSupersedesWorkingTextAndPreservesCreditAsync(string workingState)
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid authorId, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient administrator) = await ActorAsync(factory, database, "Administrator");
        using (owner)
        using (editor)
        using (administrator)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Corrections");
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Original public title");
            DateTime firstPublished = await ScalarAsync<DateTime>(database, "SELECT \"FirstPublishedAtUtc\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id);
            ArticleResponse original = article;
            article = await ActAsync(owner, article, "working-version");
            article = await SaveAsync(owner, article, Complete(category.Id, "Retain this proposal"));
            if (workingState != "Draft")
                article = await ActAsync(owner, article, "submit", "Proposed update");
            if (workingState == "Approved")
                article = await ActAsync(editor, article, "approve");
            Guid supersededId = article.RevisionId;
            ArticleResponse corrected = await CorrectAsync(administrator, article, Complete(category.Id, "Editorial correction"), "Corrected a verified error");
            Assert.Equal("Published", corrected.State);
            Assert.Equal("Editorial correction", corrected.Content.Title);
            Assert.NotEqual(supersededId, corrected.RevisionId);
            Assert.Equal(authorId, await ScalarAsync<Guid>(database, "SELECT \"AuthorId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            Assert.Equal(firstPublished, await ScalarAsync<DateTime>(database, "SELECT \"FirstPublishedAtUtc\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            Assert.Equal("Superseded", await ScalarAsync<string>(database, "SELECT \"State\" FROM news.\"Revisions\" WHERE \"Id\"=@id", supersededId));
            Assert.Equal("Retain this proposal", await ScalarAsync<string>(database, "SELECT \"Title\" FROM news.\"Revisions\" WHERE \"Id\"=@id", supersededId));
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, article, "publish")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, corrected, "publish")).StatusCode);
            NewsPage<ArticleHistoryResponse> history = (await owner.GetFromJsonAsync<NewsPage<ArticleHistoryResponse>>($"/api/editorial/articles/{article.Id}/history"))!;
            Assert.Contains(history.Items, item => item.Revision.RevisionId == supersededId && item.Reason == "Corrected a verified error");
            Assert.Contains(history.Items, item => item.Revision.RevisionId == original.RevisionId && item.Revision.Content.Title == "Original public title");
            string notices = await owner.GetStringAsync("/api/account/notifications");
            Assert.Contains("article.corrected", notices);
            Assert.Contains("article.superseded", notices);
            Assert.DoesNotContain("Retain this proposal", notices);
            ArticleResponse fresh = await ActAsync(owner, corrected, "working-version");
            Assert.Equal("Draft", fresh.State);
            Assert.Equal("Editorial correction", fresh.Content.Title);
            Assert.NotEqual(supersededId, fresh.RevisionId);
            using JsonDocument visible = await PublicAsync(anonymous, article.Id);
            Assert.Equal("Editorial correction", visible.RootElement.GetProperty("content").GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task Published_CorrectionValidationPermissionsAndWithdrawnState_DoNotMutateAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Validation");
            ArticleResponse privateDraft = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Private"));
            Assert.Equal(HttpStatusCode.NotFound, (await CorrectionResponseAsync(editor, privateDraft, Complete(category.Id, "Forbidden"), "No private bypass")).StatusCode);
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Validated publication");
            ArticleContent replacement = Complete(category.Id, "Replacement");
            Assert.Equal(HttpStatusCode.Unauthorized, (await CorrectionResponseAsync(anonymous, article, replacement, "No session")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await CorrectionResponseAsync(owner, article, replacement, "No editorial role")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await CorrectionResponseAsync(editor, article, replacement, "  ")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await CorrectionResponseAsync(editor, article, new ArticleContent(), "Incomplete")).StatusCode);
            Assert.Equal(article.Version, (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!.Version);
            article = await ActAsync(editor, article, "withdraw", "Needs correction");
            Assert.Equal(HttpStatusCode.Conflict, (await CorrectionResponseAsync(editor, article, replacement, "Cannot rewrite withdrawn")).StatusCode);
            editor.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await CorrectionResponseAsync(editor, article, replacement, "Missing CSRF")).StatusCode);
        }
    }

    [Fact]
    public async Task Published_PublicReadsAliasesAndWithdrawal_ExposeOnlyCurrentPublicContentAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Public reads");
            ArticleResponse draft = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Never published secret"));
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/news/articles/{draft.Id}")).StatusCode);
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Original public title");
            using JsonDocument original = await PublicAsync(anonymous, article.Id);
            string firstSlug = original.RootElement.GetProperty("slug").GetString()!;
            string firstDate = original.RootElement.GetProperty("firstPublishedAtUtc").GetString()!;
            string publicUpdated = original.RootElement.GetProperty("updatedAtUtc").GetString()!;
            article = await ActAsync(owner, article, "working-version");
            article = await SaveAsync(owner, article, Complete(category.Id, "Unsubmitted private title") with { SeoTitle = "Private SEO" });
            using JsonDocument pending = await PublicAsync(anonymous, article.Id);
            Assert.Equal("Original public title", pending.RootElement.GetProperty("content").GetProperty("title").GetString());
            Assert.DoesNotContain("Private SEO", pending.RootElement.GetRawText());
            Assert.Equal(publicUpdated, pending.RootElement.GetProperty("updatedAtUtc").GetString());
            AccountResponse profile = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
            Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync("/api/account/display-name", new { profile.Version, displayName = "UpdatedPublicCredit" })).StatusCode);
            using JsonDocument renamed = await PublicAsync(anonymous, article.Id);
            Assert.Equal("UpdatedPublicCredit", renamed.RootElement.GetProperty("authorDisplayName").GetString());
            Assert.DoesNotContain(profile.Email, renamed.RootElement.GetRawText());
            Assert.Equal(publicUpdated, renamed.RootElement.GetProperty("updatedAtUtc").GetString());
            ArticleResponse corrected = await CorrectAsync(editor, article, Complete(category.Id, "Current corrected title"), "Verified correction");
            using JsonDocument current = await PublicAsync(anonymous, article.Id);
            string currentSlug = current.RootElement.GetProperty("slug").GetString()!;
            Assert.NotEqual(firstSlug, currentSlug);
            Assert.Equal(firstDate, current.RootElement.GetProperty("firstPublishedAtUtc").GetString());
            using JsonDocument alias = await PublicAsync(anonymous, article.Id, firstSlug);
            Assert.True(alias.RootElement.GetProperty("requestedSlugIsAlias").GetBoolean());
            Assert.Equal(current.RootElement.GetProperty("canonicalUrl").GetString(), alias.RootElement.GetProperty("canonicalUrl").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/news/articles/{article.Id}?slug=unsubmitted-private-title")).StatusCode);
            corrected = await ActAsync(editor, corrected, "withdraw", "Private withdrawal reason");
            foreach (string slug in new[] { firstSlug, currentSlug })
            {
                using HttpResponseMessage hidden = await anonymous.GetAsync($"/api/news/articles/{article.Id}?slug={Uri.EscapeDataString(slug)}");
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
                Assert.True(hidden.Headers.CacheControl?.NoStore);
                string body = await hidden.Content.ReadAsStringAsync();
                Assert.Contains("article_withdrawn", body);
                Assert.DoesNotContain("Current corrected title", body);
                Assert.DoesNotContain("UpdatedPublicCredit", body);
                Assert.DoesNotContain("Private withdrawal reason", body);
                Assert.DoesNotContain("Original public title", body);
            }
        }
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("withdraw")]
    public async Task Published_CorrectionVersusPublicationOrWithdrawal_HasOneVersionWinnerAsync(string competingAction)
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Transition races");
            ImageResponse image = await UploadAsync(owner);
            await ProcessImagesAsync(factory);
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Before race", new ArticleImage(image.Id, "Race cover"));
            article = await ActAsync(owner, article, "working-version");
            article = await SaveAsync(owner, article, Complete(category.Id, "Approved proposal"));
            article = await ActAsync(owner, article, "submit", "Ready replacement");
            article = await ActAsync(editor, article, "approve");
            HttpResponseMessage[] responses = await Task.WhenAll(CorrectionResponseAsync(editor, article, Complete(category.Id, "Direct correction winner"), "Concurrent correction"),
                ActResponseAsync(competingAction == "publish" ? owner : editor, article, competingAction, "Concurrent withdrawal"));
            Assert.Single(responses, item => item.IsSuccessStatusCode);
            Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Conflict);
            ArticleResponse state = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal(article.Version + 1, state.Version);
            bool corrected = responses[0].IsSuccessStatusCode;
            using HttpResponseMessage publicResponse = await anonymous.GetAsync($"/api/news/articles/{article.Id}");
            if (!corrected && competingAction == "withdraw")
            {
                Assert.Equal("Withdrawn", state.PublicationState);
                Assert.Equal("Draft", state.State);
                Assert.Equal(HttpStatusCode.NotFound, publicResponse.StatusCode);
            }
            else
            {
                Assert.Equal("Published", state.PublicationState);
                Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
                using var body = JsonDocument.Parse(await publicResponse.Content.ReadAsStringAsync());
                Assert.Equal(corrected ? "Direct correction winner" : "Approved proposal", body.RootElement.GetProperty("content").GetProperty("title").GetString());
            }
            Assert.Equal(corrected ? "Superseded" : competingAction == "publish" ? "Published" : "Approved", await ScalarAsync<string>(database, "SELECT \"State\" FROM news.\"Revisions\" WHERE \"Id\"=@id", article.RevisionId));
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            foreach (HttpResponseMessage response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Published_CorrectionVersusEditorialRoleRevocation_RechecksCurrentPermissionAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid editorId, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient administrator) = await ActorAsync(factory, database, "Administrator");
        using (owner)
        using (editor)
        using (administrator)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Correction permission race");
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Before permission race");
            AccountSummary account = (await administrator.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{editorId}"))!;
            HttpResponseMessage[] responses = await Task.WhenAll(CorrectionResponseAsync(editor, article, Complete(category.Id, "Authorized before removal"), "Permission race"),
                administrator.PutAsJsonAsync($"/api/admin/accounts/{editorId}/roles", new { account.Version, roles = new[] { "RegularUser" } }));
            Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
            Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden });
            using JsonDocument body = await PublicAsync(anonymous, article.Id);
            Assert.Equal(responses[0].IsSuccessStatusCode ? "Authorized before removal" : "Before permission race", body.RootElement.GetProperty("content").GetProperty("title").GetString());
            ArticleResponse current = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal(HttpStatusCode.Forbidden, (await CorrectionResponseAsync(editor, current, Complete(category.Id, "Revoked bypass"), "No permission")).StatusCode);
            Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"Articles\" WHERE \"Id\"=@id AND \"ReviewerId\" IS NOT NULL", article.Id));
            foreach (HttpResponseMessage response in responses)
                response.Dispose();
        }
    }

    [Theory]
    [InlineData("correction")]
    [InlineData("withdraw")]
    public async Task Published_NotificationFailureRollsBackPublicTextHistoryAndMediaAsync(string action)
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Publication rollback");
            ImageResponse image = await UploadAsync(owner);
            await ProcessImagesAsync(factory);
            ArticleResponse published = await PublishedAsync(owner, editor, category.Id, "Before failed transaction", new ArticleImage(image.Id, "River"));
            ArticleResponse article = await ActAsync(owner, published, "working-version");
            article = await SaveAsync(owner, article, Complete(category.Id, "Retained private proposal"));
            long historyBefore = await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"History\" WHERE \"ArticleId\"=@id", article.Id);
            long revisionCountBefore = await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"Revisions\" WHERE \"ArticleId\"=@id", article.Id);
            await ExecuteAsync(database, "ALTER TABLE notifications.\"EmailDeliveries\" ADD CONSTRAINT reject_published_workflow CHECK (\"Purpose\" <> 'workflow') NOT VALID", Guid.Empty);
            using HttpResponseMessage failed = action == "correction"
                ? await CorrectionResponseAsync(editor, article, Complete(category.Id, "Must roll back"), "Failure check")
                : await ActResponseAsync(editor, article, "withdraw", "Must roll back");
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            ArticleResponse unchanged = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal(article.Version, unchanged.Version);
            Assert.Equal(article.RevisionId, unchanged.RevisionId);
            Assert.Equal("Draft", unchanged.State);
            Assert.Equal("Retained private proposal", unchanged.Content.Title);
            Assert.Equal(published.RevisionId, await ScalarAsync<Guid>(database, "SELECT \"PublishedRevisionId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            Assert.Equal(historyBefore, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"History\" WHERE \"ArticleId\"=@id", article.Id));
            Assert.Equal(revisionCountBefore, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"Revisions\" WHERE \"ArticleId\"=@id", article.Id));
            using JsonDocument body = await PublicAsync(anonymous, article.Id);
            Assert.Equal("Before failed transaction", body.RootElement.GetProperty("content").GetProperty("title").GetString());
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            await ExecuteAsync(database, "ALTER TABLE notifications.\"EmailDeliveries\" DROP CONSTRAINT reject_published_workflow", Guid.Empty);
        }
    }

    [Fact]
    public async Task Published_SharedImageReferencesAndTemplateBlocks_EnforceCurrentVisibilityAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor", "Moderator");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Shared publication image");
            ImageResponse image = await UploadAsync(owner);
            await ProcessImagesAsync(factory);
            using HttpResponseMessage created = await owner.PostAsJsonAsync("/api/media/templates", new { imageId = image.Id, name = "Shared public river" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            TemplateResponse template = (await created.Content.ReadFromJsonAsync<TemplateResponse>())!;
            template = await TemplateActionAsync(owner, template, "submit");
            template = await TemplateActionAsync(editor, template, "approve");
            ArticleResponse first = await PublishedAsync(owner, editor, category.Id, "First shared image", new ArticleImage(image.Id, "First river"));
            ArticleResponse second = await PublishedAsync(owner, editor, category.Id, "Second shared image", new ArticleImage(image.Id, "Second river"));
            first = await ActAsync(editor, first, "withdraw", "First withdrawn");
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            template = await TemplateActionAsync(editor, template, "block", "Blocked visual");
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            template = await TemplateActionAsync(editor, template, "unblock", "Visual cleared");
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            second = await CorrectAsync(editor, second, Complete(category.Id, "Image removed by correction"), "Removed cover");
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
        }
    }

    [Fact]
    public async Task Published_FormerAuthorWithEditorRoleCannotReadPrivateProposalOnWithdrawalAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid ownerId, HttpClient owner) = await ActorAsync(factory, database, "Author", "Editor");
        (_, HttpClient administrator) = await ActorAsync(factory, database, "Administrator");
        using (owner)
        using (administrator)
        {
            TaxonomyResponse category = await TaxonomyAsync(owner, "categories", "Role-sensitive withdrawal");
            ArticleResponse article = await PublishedAsync(owner, owner, category.Id, "Published before role loss");
            article = await ActAsync(owner, article, "working-version");
            article = await SaveAsync(owner, article, Complete(category.Id, "Private draft after publication"));
            AccountSummary account = (await administrator.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{ownerId}"))!;
            Assert.Equal(HttpStatusCode.OK, (await administrator.PutAsJsonAsync($"/api/admin/accounts/{ownerId}/roles", new { account.Version, roles = new[] { "Editor" } })).StatusCode);
            ArticleResponse withdrawn = await ActAsync(owner, article, "withdraw", "Editorial withdrawal after role loss");
            Assert.Equal("Published before role loss", withdrawn.Content.Title);
            Assert.Equal(HttpStatusCode.Forbidden, (await ActResponseAsync(owner, withdrawn, "submit", "Cannot correct as author")).StatusCode);
        }
    }

    [Fact]
    public async Task Published_EditorMediaSelectionUsesActorUploadsAndRetainsOriginalOwnershipAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid authorId, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Author", "Editor");
        (_, HttpClient foreign) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editorOnly) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (foreign)
        using (editorOnly)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Editorial media");
            ImageResponse authorImage = await UploadAsync(owner);
            ImageResponse editorialImage = await UploadAsync(editor);
            ImageResponse foreignImage = await UploadAsync(foreign);
            await ProcessImagesAsync(factory);
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Original author's media", new ArticleImage(authorImage.Id, "Original river"));
            article = await CorrectAsync(editorOnly, article, article.Content with { Title = "Editor retains original cover" }, "Retained author cover");
            Assert.Equal(authorImage.Id, article.Content.Cover?.MediaId);
            Assert.Equal(HttpStatusCode.NotFound, (await CorrectionResponseAsync(editor, article, Complete(category.Id, "Foreign media bypass") with { Cover = new ArticleImage(foreignImage.Id, "Not selectable") }, "Foreign upload")).StatusCode);
            article = await CorrectAsync(editor, article, Complete(category.Id, "Editorial cover") with { Cover = new ArticleImage(editorialImage.Id, "Editorial river") }, "Editorial replacement");
            Assert.Equal(authorId, await ScalarAsync<Guid>(database, "SELECT \"AuthorId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{editorialImage.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{authorImage.Id}/480")).StatusCode);
            ArticleResponse proposal = await ActAsync(owner, article, "working-version");
            Assert.Equal(editorialImage.Id, proposal.Content.Cover?.MediaId);
            proposal = await SaveAsync(owner, proposal, proposal.Content with { Title = "Author retains editorial cover" });
            Assert.Equal(editorialImage.Id, proposal.Content.Cover?.MediaId);
        }
    }

    [Fact]
    public async Task Published_EditorialCorrectionAfterFinalRejectionPreservesTerminalAuthorStateAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Final rejection remains final");
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Current before rejection");
            article = await ActAsync(owner, article, "working-version");
            article = await SaveAsync(owner, article, Complete(category.Id, "Rejected proposal text"));
            article = await ActAsync(owner, article, "submit", "Proposed correction");
            article = await ActAsync(editor, article, "reject", "Unverifiable proposal");
            Guid rejectedId = article.RevisionId;
            ArticleResponse corrected = await CorrectAsync(editor, article, Complete(category.Id, "Safe editorial correction"), "Checked independently");
            ArticleResponse current = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal("FinalRejected", current.State);
            Assert.Equal(rejectedId, current.RevisionId);
            Assert.Equal("Rejected proposal text", current.Content.Title);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, current, "working-version")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, current, "submit", "Retry after editorial correction")).StatusCode);
            Assert.DoesNotContain("article.superseded", await owner.GetStringAsync("/api/account/notifications"));
            using JsonDocument body = await PublicAsync(anonymous, article.Id);
            Assert.Equal("Safe editorial correction", body.RootElement.GetProperty("content").GetProperty("title").GetString());
        }
    }

    private static async Task ProcessImagesAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
    }

    private static async Task<TemplateResponse> TemplateActionAsync(HttpClient client, TemplateResponse template, string action, string? reason = null)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/media/templates/{template.Id}/{action}", new { template.Version, reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateResponse>())!;
    }

    private static async Task<ArticleResponse> PublishedAsync(HttpClient owner, HttpClient editor, Guid categoryId, string title, ArticleImage? cover = null)
    {
        ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(categoryId, title) with { Cover = cover });
        article = await ActAsync(owner, article, "submit");
        article = await ActAsync(editor, article, "claim");
        article = await ActAsync(editor, article, "approve");
        return await ActAsync(owner, article, "publish");
    }

    private static Task<HttpResponseMessage> CorrectionResponseAsync(HttpClient actor, ArticleResponse article, ArticleContent content, string reason) =>
        actor.PostAsJsonAsync($"/api/editorial/articles/{article.Id}/corrections", new { article.Version, content, reason });

    private static async Task<ArticleResponse> CorrectAsync(HttpClient actor, ArticleResponse article, ArticleContent content, string reason)
    {
        using HttpResponseMessage response = await CorrectionResponseAsync(actor, article, content, reason);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"correction: {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ArticleResponse>())!;
    }

    private static async Task<JsonDocument> PublicAsync(HttpClient client, Guid id, string? slug = null)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/news/articles/{id}" + (slug is null ? "" : $"?slug={Uri.EscapeDataString(slug)}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
