using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Classifieds;
using CityPortal.News;
using CityPortal.Notifications;
using CityPortal.Reports;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class ContentReportsTests : IClassFixture<RegistrationFixture>
{
    private const string _queue = "/api/staff/reports";
    private readonly RegistrationFixture _fixture;

    public ContentReportsTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task QueuesGroupByKindAndDismissOnlyReportsInThePreviewAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "ReportOwner", confirmed: true, role: "Author");
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "ReportAdmin", confirmed: true, role: "Administrator");
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "ReportModerator", confirmed: true, role: "Moderator");
        using HttpClient visitor = await VisitorAsync(factory);
        ListingDraftResponse listing = await PublishListingAsync(owner, admin);
        TaxonomyResponse category = await NewsTests.TaxonomyAsync(admin, "categories", "Reports");
        ArticleResponse article = await NewsTests.PublishedAsync(owner, admin, category.Id, "Reported article");
        string listingReview = $"{_queue}/classifieds/{listing.Id}";
        string articleReview = $"{_queue}/articles/{article.Id}";
        string articleAddress = ArticleAddress(article);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.PostAsJsonAsync($"/api/reports/classifieds/{Guid.NewGuid()}",
            new SubmitContentReportRequest { Category = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(visitor, "articles", article.Id, "wrong-category")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, "articles", article.Id, "copyright")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, "classifieds", listing.Id, "incorrect-price")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync($"/api/classifieds/{listing.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(listingReview)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(listingReview)).StatusCode);

        ReportReviewPreview first = (await moderator.GetFromJsonAsync<ReportReviewPreview>(listingReview))!;
        Assert.Equal("incorrect-price", Assert.Single(first.Reports).Category);
        Assert.Null(first.Reports[0].Text);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(owner, "classifieds", listing.Id, "fraud", "New report")).StatusCode);
        ReportedContentSummary group = Assert.Single((await admin.GetFromJsonAsync<ReportQueuePage>(_queue + "/classifieds"))!.Items);
        Assert.Equal((listing.Id, 2, 2), (group.Id, group.Total, group.Pending));
        Assert.Equal(article.Id, Assert.Single((await moderator.GetFromJsonAsync<ReportQueuePage>(_queue + "/articles"))!.Items).Id);

        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(moderator, listingReview, first, "dismiss")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(admin, listingReview, first, "dismiss")).StatusCode);
        ReportReviewPreview reopened = (await admin.GetFromJsonAsync<ReportReviewPreview>(listingReview))!;
        Assert.Equal("New report", Assert.Single(reopened.Reports, report => report.DecisionId is null).Text);
        Assert.Equal("dismiss", Assert.Single(reopened.Decisions).Outcome);

        ReportReviewPreview articlePreview = (await moderator.GetFromJsonAsync<ReportReviewPreview>(articleReview))!;
        Assert.Equal(article.PublicAddress, articlePreview.PublicUrl);
        Assert.Contains("Verified body text", articlePreview.Text);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(articleAddress)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(moderator, articleReview, articlePreview, "hide")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(moderator, articleReview, articlePreview, "hide", "Remove copyrighted copy.")).StatusCode);
        using HttpResponseMessage withdrawn = await visitor.GetAsync(articleAddress);
        Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
        Assert.Equal("article_withdrawn", (await withdrawn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty((await admin.GetFromJsonAsync<ReportQueuePage>(_queue + "/articles"))!.Items);
    }

    [Fact]
    public async Task HidingRollsBackWithFailedDecisionAndKeepsHistoryAfterDeletionAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "ListingOwner", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "ListingAdmin", confirmed: true, role: "Administrator");
        using HttpClient visitor = await VisitorAsync(factory);
        ListingDraftResponse listing = await PublishListingAsync(owner, admin);
        string publicPath = $"/api/classifieds/{listing.Id}";
        string previewPath = $"{_queue}/classifieds/{listing.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, "classifieds", listing.Id, "spam", "Bulk posting")).StatusCode);
        ReportReviewPreview preview = (await admin.GetFromJsonAsync<ReportReviewPreview>(previewPath))!;
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var injectFailure = new NpgsqlCommand("""
            CREATE FUNCTION reports.fail_test_decision() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'forced test rollback';
            END;
            $$;
            CREATE TRIGGER fail_test_decision BEFORE INSERT ON reports."Decisions"
            FOR EACH ROW EXECUTE FUNCTION reports.fail_test_decision();
            """, connection);
        await injectFailure.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, (await DecideAsync(admin, previewPath, preview, "hide", "Spam")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(publicPath)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<ReportReviewPreview>(previewPath))!.Decisions);
        AccountNotificationPage before = (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!;
        Assert.Equal("listing.approved", Assert.Single(before.Items).Type);
        await using var removeFailure = new NpgsqlCommand("""DROP TRIGGER fail_test_decision ON reports."Decisions" """, connection);
        await removeFailure.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(admin, previewPath, preview, "hide", "Spam")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(visitor, "classifieds", listing.Id, "spam")).StatusCode);
        AccountNotificationPage after = (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!;
        AccountNotificationResponse notice = Assert.Single(after.Items, item => item.Type == "listing.blocked");
        Assert.Equal(listing.Id, notice.ResourceId);
        Assert.DoesNotContain("Spam", notice.Message, StringComparison.OrdinalIgnoreCase);

        string ownerPath = $"/api/account/classifieds/drafts/{listing.Id}";
        ListingDraftResponse blocked = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{ownerPath}?version={blocked.Version}")).StatusCode);
        ReportReviewPreview history = (await admin.GetFromJsonAsync<ReportReviewPreview>(previewPath))!;
        Assert.Null(history.PublicUrl);
        Assert.Equal("Desk", history.Title);
        Assert.Equal("Bulk posting", Assert.Single(history.Reports).Text);
        Assert.Equal("Spam", Assert.Single(history.Decisions).Reason);
        ReportQueuePage archive = (await admin.GetFromJsonAsync<ReportQueuePage>(_queue + "/classifieds?history=true"))!;
        Assert.Equal(listing.Id, Assert.Single(archive.Items).Id);
    }

    [Fact]
    public async Task AnonymousReportsRequireCsrfAndEnforceTheHourlyLimitAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "ListingOwner", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "ListingAdmin", confirmed: true, role: "Administrator");
        using HttpClient visitor = await VisitorAsync(factory);
        using HttpClient noToken = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        ListingDraftResponse listing = await PublishListingAsync(owner, admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(noToken, "classifieds", listing.Id, "spam")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(visitor, "classifieds", listing.Id, "spam", new string('a', 501))).StatusCode);
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        for (int index = 0; index < 5; index++)
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, "classifieds", listing.Id, "other")).StatusCode);
        using HttpResponseMessage limited = await SubmitAsync(visitor, "classifieds", listing.Id, "other");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(visitor, "classifieds", listing.Id, "other")).StatusCode);
    }

    private static async Task<HttpClient> VisitorAsync(ApiFactory factory)
    {
        HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        JsonElement csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return client;
    }

    private static async Task<ListingDraftResponse> PublishListingAsync(HttpClient owner, HttpClient admin)
    {
        ListingDraftResponse submitted = await ClassifiedsPublicationTests.SubmitAsync(owner, admin, "Central");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/staff/classifieds/review/{submitted.Id}/approve",
            new ListingDraftVersionRequest { Version = submitted.Version })).StatusCode);
        return submitted;
    }

    private static string ArticleAddress(ArticleResponse article) => "/api/news/articles/by-address/" + article.PublicAddress!["/news/".Length..];

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string kind, Guid id, string category, string? text = null) =>
        client.PostAsJsonAsync($"/api/reports/{kind}/{id}", new SubmitContentReportRequest { Category = category, Text = text });

    private static Task<HttpResponseMessage> DecideAsync(HttpClient staff, string path, ReportReviewPreview preview, string outcome, string? reason = null) =>
        staff.PostAsJsonAsync(path + "/decide", new DecideContentReportsRequest
        {
            Outcome = outcome,
            ThroughSequence = preview.ThroughSequence!.Value,
            TargetVersion = preview.TargetVersion,
            Reason = reason
        });
}
