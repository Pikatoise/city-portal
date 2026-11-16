using System.Net;
using System.Net.Http.Json;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Fact]
    public async Task Published_WithdrawalByAnotherEditorDoesNotExposePrivateProposalAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient other) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (other)
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Private proposal");
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Public title"));
            article = await ActAsync(owner, article, "submit");
            article = await ActAsync(editor, article, "claim");
            article = await ActAsync(editor, article, "approve");
            article = await ActAsync(owner, article, "publish");
            article = await ActAsync(owner, article, "propose");
            article = await SaveAsync(owner, article, Complete(category.Id, "Private unsubmitted title"));
            ArticleResponse result = await ActAsync(other, article, "withdraw", "Verified error");
            Assert.Equal("Public title", result.Content.Title);
            ArticleResponse retained = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal("Private unsubmitted title", retained.Content.Title);
            Assert.Equal("Draft", retained.State);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/editorial/reviews/{article.Id}")).StatusCode);
            NewsPage<ArticleHistoryResponse> history = (await editor.GetFromJsonAsync<NewsPage<ArticleHistoryResponse>>($"/api/editorial/articles/{article.Id}/history"))!;
            Assert.DoesNotContain(history.Items, item => item.Revision.Content.Title == "Private unsubmitted title");
            Assert.Contains(history.Items, item => item.Action == "withdraw" && item.Reason == "Verified error");
        }
    }

    [Fact]
    public async Task Published_ProposalKeepsPublicRevisionAndReplacementKeepsFirstPublicationAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Revisions");
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Original"));
            article = await ActAsync(owner, article, "submit");
            article = await ActAsync(editor, article, "claim");
            article = await ActAsync(editor, article, "approve");
            article = await ActAsync(owner, article, "publish");
            Guid original = article.RevisionId;
            DateTime first = await ScalarAsync<DateTime>(database, "SELECT \"FirstPublishedAtUtc\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id);
            ArticleResponse proposal = await ActAsync(owner, article, "propose");
            Assert.NotEqual(original, proposal.RevisionId);
            Assert.Equal("Draft", proposal.State);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, proposal, "propose")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ActResponseAsync(editor, proposal, "propose")).StatusCode);
            proposal = await SaveAsync(owner, proposal, Complete(category.Id, "Replacement"));
            proposal = await ActAsync(owner, proposal, "submit", "Updated facts.");
            proposal = await ActAsync(editor, proposal, "approve");
            Assert.Equal(original, await ScalarAsync<Guid>(database, "SELECT \"PublishedRevisionId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            proposal = await ActAsync(owner, proposal, "publish");
            Assert.Equal(proposal.RevisionId, await ScalarAsync<Guid>(database, "SELECT \"PublishedRevisionId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            Assert.Equal(first, await ScalarAsync<DateTime>(database, "SELECT \"FirstPublishedAtUtc\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, article, "propose")).StatusCode);
        }
    }

    [Fact]
    public async Task Published_WithdrawalResetsReviewClosesPublicMediaAndRequiresApprovalAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Withdrawal");
            ImageResponse image = await UploadAsync(owner);
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Published cover") with { Cover = new ArticleImage(image.Id, "River") });
            article = await ActAsync(owner, article, "submit");
            article = await ActAsync(editor, article, "claim");
            article = await ActAsync(editor, article, "approve");
            article = await ActAsync(owner, article, "publish");
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            article = await ActAsync(owner, article, "propose");
            article = await ActAsync(owner, article, "submit", "Correction proposal");
            article = await ActAsync(editor, article, "approve");
            Assert.Equal(HttpStatusCode.BadRequest, (await ActResponseAsync(editor, article, "withdraw")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ActResponseAsync(owner, article, "withdraw", "Incorrect")).StatusCode);
            ArticleResponse withdrawn = await ActAsync(editor, article, "withdraw", "Incorrect facts");
            Assert.Equal("Withdrawn", withdrawn.PublicationState);
            withdrawn = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal("Draft", withdrawn.State);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, withdrawn, "publish")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(editor, article, "publish")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await editor.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { withdrawn.Version, content = Complete(category.Id, "Editor bypass") })).StatusCode);
            withdrawn = await ActAsync(owner, withdrawn, "submit", "Verified corrections");
            withdrawn = await ActAsync(editor, withdrawn, "approve");
            withdrawn = await ActAsync(owner, withdrawn, "publish");
            Assert.Equal("Published", withdrawn.PublicationState);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
        }
    }

    [Fact]
    public async Task Review_RoleRemovalVersusApproval_SerializesAuthorizationAndAssignmentAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid reviewerId, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient admin) = await ActorAsync(factory, database, "Administrator");
        using (owner)
        using (editor)
        using (admin)
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Role race");
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Concurrent permission change"));
            article = await ActAsync(owner, article, "submit");
            article = await ActAsync(editor, article, "claim");
            AccountSummary account = (await admin.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{reviewerId}"))!;
            HttpResponseMessage[] results = await Task.WhenAll(ActResponseAsync(editor, article, "approve"), admin.PutAsJsonAsync($"/api/admin/accounts/{reviewerId}/roles", new { account.Version, roles = new[] { "RegularUser" } }));
            Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
            Assert.Contains(results[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden });
            Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"Articles\" WHERE \"Id\"=@id AND \"ReviewerId\" IS NOT NULL", article.Id));
            ArticleResponse current = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal(results[0].IsSuccessStatusCode ? "Approved" : "InReview", current.State);
            Assert.Equal(article.Version + (results[0].IsSuccessStatusCode ? 2 : 1), current.Version);
            Assert.Equal(HttpStatusCode.Forbidden, (await ActResponseAsync(editor, current, "approve")).StatusCode);
            foreach (HttpResponseMessage result in results)
                result.Dispose();
        }
    }

    [Fact]
    public async Task Review_MediaAudienceChangesAndPublicationDeletionRace_AreAtomicAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient first) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient second) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (first)
        using (second)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(first, "categories", "Image audience");
            ImageResponse image = await UploadAsync(owner);
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Private cover") with { Cover = new ArticleImage(image.Id, "River") });
            article = await ActAsync(owner, article, "submit");
            Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            article = await ActAsync(first, article, "claim");
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            article = await ActAsync(second, article, "takeover");
            Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await second.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            article = await ActAsync(second, article, "approve");
            HttpResponseMessage[] results = await Task.WhenAll(ActResponseAsync(owner, article, "publish"), owner.DeleteAsync($"/api/editorial/articles/{article.Id}?version={article.Version}"));
            Assert.Single(results, item => item.IsSuccessStatusCode);
            bool published = results[0].IsSuccessStatusCode;
            Assert.Equal(published ? HttpStatusCode.Conflict : HttpStatusCode.NotFound, results[published ? 1 : 0].StatusCode);
            Assert.Equal(published ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            Assert.False(await ScalarAsync<bool>(database, "SELECT \"DeletedAtUtc\" IS NOT NULL AND \"PublishedRevisionId\" IS NOT NULL FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            foreach (HttpResponseMessage result in results)
                result.Dispose();
        }
    }

    [Fact]
    public async Task Review_NotificationFailure_RollsBackDecisionAndRoleReleaseAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid reviewerId, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient admin) = await ActorAsync(factory, database, "Administrator");
        using (owner)
        using (editor)
        using (admin)
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Atomic decisions");
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Rollback test"));
            article = await ActAsync(owner, article, "submit");
            article = await ActAsync(editor, article, "claim");
            await ExecuteAsync(database, "ALTER TABLE notifications.\"EmailDeliveries\" ADD CONSTRAINT reject_news_workflow CHECK (\"Purpose\" <> 'workflow')", Guid.Empty);
            Assert.Equal(HttpStatusCode.InternalServerError, (await ActResponseAsync(editor, article, "approve")).StatusCode);
            ArticleResponse unchanged = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            Assert.Equal(article.Version, unchanged.Version);
            Assert.Equal("InReview", unchanged.State);
            Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"History\" WHERE \"ArticleId\"=@id AND \"Action\"='approve'", article.Id));
            AccountSummary account = (await admin.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{reviewerId}"))!;
            Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PutAsJsonAsync($"/api/admin/accounts/{reviewerId}/roles", new { account.Version, roles = new[] { "RegularUser" } })).StatusCode);
            Assert.Equal(reviewerId, await ScalarAsync<Guid>(database, "SELECT \"ReviewerId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            Assert.Contains("Editor", (await admin.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{reviewerId}"))!.Roles);
            await ExecuteAsync(database, "ALTER TABLE notifications.\"EmailDeliveries\" DROP CONSTRAINT reject_news_workflow", Guid.Empty);
            article = await ActAsync(editor, article, "approve");
            Assert.Equal("Approved", article.State);
            Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"History\" WHERE \"ArticleId\"=@id AND \"Action\"='approve'", article.Id));
        }
    }

    [Fact]
    public async Task Review_ReturnAndResubmit_PreserveSnapshotAndPrivateUnsentChangesAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid editorId, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient other) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (other)
        {
            ArticleResponse article = await CreateAsync(owner);
            Assert.Equal(HttpStatusCode.BadRequest, (await ActResponseAsync(owner, article, "submit")).StatusCode);
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Review category");
            article = await SaveAsync(owner, article, Complete(category.Id, "Submitted title"));
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/editorial/articles/{article.Id}")).StatusCode);
            Guid draftId = article.RevisionId;
            article = await ActAsync(owner, article, "submit");
            Assert.Equal("InReview", article.State);
            Assert.NotEqual(draftId, article.RevisionId);
            Assert.True(article.PreviouslySubmitted);
            Assert.Contains((await editor.GetFromJsonAsync<NewsPage<ReviewSummaryResponse>>("/api/editorial/review-queue"))!.Items, item => item.Id == article.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"/api/editorial/articles/{article.Id}/history")).StatusCode);
            article = await ActAsync(editor, article, "claim");
            Guid submittedId = article.RevisionId;
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content = Complete(category.Id, "Blocked edit") })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await ActResponseAsync(editor, article, "return", "  ")).StatusCode);
            article = await ActAsync(editor, article, "return", "Please check attribution.");
            Assert.Equal("Draft", article.State);
            Assert.NotEqual(submittedId, article.RevisionId);
            article = await SaveAsync(owner, article, Complete(category.Id, "Secret unsent changes"));
            ReviewResponse visible = (await editor.GetFromJsonAsync<ReviewResponse>($"/api/editorial/reviews/{article.Id}"))!;
            Assert.Equal("Submitted title", visible.Article.Content.Title);
            Assert.Equal("Draft", visible.WorkingState);
            string history = await editor.GetStringAsync($"/api/editorial/articles/{article.Id}/history");
            Assert.Contains("Please check attribution.", history);
            Assert.DoesNotContain("Secret unsent changes", history);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/editorial/articles/{article.Id}")).StatusCode);
            Assert.DoesNotContain((await editor.GetFromJsonAsync<NewsPage<ReviewSummaryResponse>>("/api/editorial/review-queue"))!.Items, item => item.Id == article.Id);
            Assert.Equal(HttpStatusCode.BadRequest, (await ActResponseAsync(owner, article, "submit")).StatusCode);
            article = await ActAsync(owner, article, "submit", "Checked the source.");
            visible = (await editor.GetFromJsonAsync<ReviewResponse>($"/api/editorial/reviews/{article.Id}"))!;
            Assert.Equal(editorId, visible.ReviewerId);
            Assert.Equal("Secret unsent changes", visible.Article.Content.Title);
            Assert.Equal("Submitted title", await ScalarAsync<string>(database, "SELECT \"Title\" FROM news.\"Revisions\" WHERE \"Id\"=@id", submittedId));
            Assert.Contains("review.resubmitted", await editor.GetStringAsync("/api/account/notifications"));
            Assert.Contains("article.returned", await owner.GetStringAsync("/api/account/notifications"));
        }
    }

    [Fact]
    public async Task Review_ApprovedSaveInvalidatesApprovalAndPublicationIsSeparateAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author", "Editor");
        (_, HttpClient publisher) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (publisher)
        {
            TaxonomyResponse category = await TaxonomyAsync(owner, "categories", "Publication");
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Original title"));
            article = await ActAsync(owner, article, "submit");
            article = await ActAsync(owner, article, "claim");
            article = await ActAsync(owner, article, "approve");
            Assert.Equal("Approved", article.State);
            Assert.Equal("NeverPublished", article.PublicationState);
            ArticleResponse approved = article;
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content = new ArticleContent() })).StatusCode);
            article = await SaveAsync(owner, article, Complete(category.Id, "Changed approved text"));
            Assert.Equal("InReview", article.State);
            Assert.NotEqual(approved.RevisionId, article.RevisionId);
            Assert.Equal("Original title", await ScalarAsync<string>(database, "SELECT \"Title\" FROM news.\"Revisions\" WHERE \"Id\"=@id", approved.RevisionId));
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(publisher, approved, "publish")).StatusCode);
            article = await ActAsync(owner, article, "approve");
            article = await ActAsync(publisher, article, "publish");
            Assert.Equal("Published", article.State);
            Assert.Equal("Published", article.PublicationState);
            Assert.Equal(article.RevisionId, await ScalarAsync<Guid>(database, "SELECT \"PublishedRevisionId\" FROM news.\"Articles\" WHERE \"Id\"=@id AND \"FirstPublishedAtUtc\" IS NOT NULL", article.Id));
            Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/editorial/articles/{article.Id}?version={article.Version}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, article, "publish")).StatusCode);
            Assert.Contains("article.approved", await owner.GetStringAsync("/api/account/notifications"));
            Assert.Contains("article.published", await owner.GetStringAsync("/api/account/notifications"));
        }
    }

    [Fact]
    public async Task Review_AssignmentRacesTakeoverReleaseAndRoleLoss_EnforceCurrentAccessAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid firstId, HttpClient first) = await ActorAsync(factory, database, "Editor");
        (Guid secondId, HttpClient second) = await ActorAsync(factory, database, "Editor", "Administrator");
        using (owner)
        using (first)
        using (second)
        {
            TaxonomyResponse category = await TaxonomyAsync(first, "categories", "Assignments");
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Assignment test"));
            article = await ActAsync(owner, article, "submit");
            HttpResponseMessage[] claims = await Task.WhenAll(ActResponseAsync(first, article, "claim"), ActResponseAsync(second, article, "claim"));
            Assert.Single(claims, item => item.StatusCode == HttpStatusCode.OK);
            Assert.Single(claims, item => item.StatusCode == HttpStatusCode.Conflict);
            article = (await claims.Single(item => item.IsSuccessStatusCode).Content.ReadFromJsonAsync<ArticleResponse>())!;
            ReviewResponse adminView = (await second.GetFromJsonAsync<ReviewResponse>($"/api/editorial/reviews/{article.Id}"))!;
            if (adminView.ReviewerId != firstId)
                article = await ActAsync(first, article, "takeover");
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(second, article, "approve")).StatusCode);
            article = await ActAsync(second, article, "takeover");
            Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/editorial/articles/{article.Id}/history")).StatusCode);
            article = await ActAsync(second, article, "release");
            article = await ActAsync(first, article, "claim");
            AccountSummary account = (await second.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{firstId}"))!;
            using HttpResponseMessage removed = await second.PutAsJsonAsync($"/api/admin/accounts/{firstId}/roles", new { account.Version, roles = new[] { "RegularUser" } });
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            Assert.Equal(0L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"Articles\" WHERE \"Id\"=@id AND \"ReviewerId\" IS NOT NULL", article.Id));
            Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync($"/api/editorial/reviews/{article.Id}")).StatusCode);
            Assert.Contains("roles-released", await second.GetStringAsync($"/api/editorial/articles/{article.Id}/history"));
            Assert.Equal(firstId, await ScalarAsync<Guid>(database, "SELECT \"ReviewerId\" FROM news.\"History\" WHERE \"ArticleId\"=@id AND \"Action\"='roles-released'", article.Id));
            Assert.Equal(secondId, await ScalarAsync<Guid>(database, "SELECT \"ActorId\" FROM news.\"History\" WHERE \"ArticleId\"=@id AND \"Action\"='roles-released'", article.Id));
            article = (await owner.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
            article = await ActAsync(second, article, "claim");
            AccountSummary own = (await second.GetFromJsonAsync<AccountSummary>($"/api/admin/accounts/{secondId}"))!;
            Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync($"/api/admin/accounts/{secondId}/roles", new { own.Version, roles = new[] { "Administrator" } })).StatusCode);
            Assert.Equal(secondId, await ScalarAsync<Guid>(database, "SELECT \"ReviewerId\" FROM news.\"Articles\" WHERE \"Id\"=@id", article.Id));
            article = await ActAsync(second, article, "approve");
            Assert.Equal("Approved", article.State);
            foreach (HttpResponseMessage claim in claims)
                claim.Dispose();
        }
    }

    [Fact]
    public async Task Review_RecallFinalRejectionAndRetainedDeletion_EnforceTransitionsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Rejections");
            ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Rejectable story"));
            article = await ActAsync(owner, article, "submit");
            article = await ActAsync(editor, article, "claim");
            article = await ActAsync(owner, article, "recall");
            Assert.Equal("Draft", article.State);
            Assert.Contains("review.withdrawn", await editor.GetStringAsync("/api/account/notifications"));
            article = await ActAsync(owner, article, "submit", "Ready after recall.");
            Assert.Equal(HttpStatusCode.BadRequest, (await ActResponseAsync(editor, article, "reject")).StatusCode);
            article = await ActAsync(editor, article, "reject", "Cannot verify the central claim.");
            Assert.Equal("FinalRejected", article.State);
            Assert.Equal(HttpStatusCode.Conflict, (await ActResponseAsync(owner, article, "submit", "Retry")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content = Complete(category.Id, "Try to change") })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/editorial/articles/{article.Id}?version={article.Version}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/editorial/articles/{article.Id}")).StatusCode);
            ReviewResponse retained = (await editor.GetFromJsonAsync<ReviewResponse>($"/api/editorial/reviews/{article.Id}"))!;
            Assert.True(retained.Deleted);
            Assert.Equal("Rejectable story", retained.Article.Content.Title);
            Assert.Contains("Cannot verify the central claim.", await editor.GetStringAsync($"/api/editorial/articles/{article.Id}/history"));
            Assert.Contains("review.deleted", await editor.GetStringAsync("/api/account/notifications"));
        }
    }

    private static ArticleContent Complete(Guid categoryId, string title) => NewsDocumentTests.Content("Verified body text") with { Title = title, Summary = "A complete summary", CategoryId = categoryId };

    private static Task<HttpResponseMessage> ActResponseAsync(HttpClient actor, ArticleResponse article, string action, string? reason = null) =>
        actor.PostAsJsonAsync($"/api/editorial/articles/{article.Id}/{action}", new { article.Version, reason });

    private static async Task<ArticleResponse> ActAsync(HttpClient actor, ArticleResponse article, string action, string? reason = null)
    {
        using HttpResponseMessage response = await ActResponseAsync(actor, article, action, reason);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{action}: {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ArticleResponse>())!;
    }
}
