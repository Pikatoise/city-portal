using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Classifieds;
using CityPortal.Identity;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class ClassifiedsPublicationTests : IClassFixture<RegistrationFixture>
{
    private const string _drafts = "/api/account/classifieds/drafts";
    private const string _review = "/api/staff/classifieds/review";
    private const string _published = "/api/staff/classifieds/published";
    private const string _blocks = "/api/staff/account-blocks";
    private readonly RegistrationFixture _fixture;

    public ClassifiedsPublicationTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FirstPublication_ExposesPhoneAndEnforcesExactExpiryAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient staff = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        ListingDraftResponse submitted = await SubmitAsync(owner, staff, "West");
        string address = $"/api/classifieds/{submitted.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(address)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(staff, submitted)).StatusCode);
        JsonElement listing = await visitor.GetFromJsonAsync<JsonElement>(address);
        Assert.Equal("+7 999 000 00 00", listing.GetProperty("phone").GetString());
        Assert.False(listing.TryGetProperty("email", out _));
        Assert.Equal(0, listing.GetProperty("photos").GetArrayLength());
        Assert.Equal(clock.GetUtcNow().ToUnixTimeMilliseconds(), listing.GetProperty("publishedAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
        Assert.Equal(clock.GetUtcNow().AddDays(7).ToUnixTimeMilliseconds(), listing.GetProperty("expiresAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(address)).StatusCode);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(address)).StatusCode);
    }

    [Fact]
    public async Task AccountBlock_RollsBackAndPreservesSessionsAndDirectBlocksAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "Moderator", confirmed: true, role: "Moderator");
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        AccountResponse account = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        AccountResponse administrator = (await admin.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        ListingDraftResponse listing = await SubmitAsync(owner, admin, "East");
        ListingDraftResponse other = await SubmitAsync(owner, admin, "South");
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(admin, listing)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(moderator, other)).StatusCode);
        string address = $"/api/classifieds/{listing.Id}";
        string otherAddress = $"/api/classifieds/{other.Id}";
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(address)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(otherAddress)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await BlockAsync(moderator, administrator)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await BlockAsync(admin, administrator)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync($"{_published}/{other.Id}/block",
            new DecideListingReviewRequest { Version = other.Version + 1, Reason = "Prohibited listing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(otherAddress)).StatusCode);

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var injectFailure = new NpgsqlCommand("""
            CREATE FUNCTION classifieds.fail_test_block() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'forced test rollback';
            END;
            $$;
            CREATE TRIGGER fail_test_block BEFORE UPDATE ON classifieds."Publications"
            FOR EACH ROW EXECUTE FUNCTION classifieds.fail_test_block();
            """, connection);
        await injectFailure.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, (await BlockAsync(moderator, account)).StatusCode);
        Assert.Null((await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!.BlockReason);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(address)).StatusCode);
        await using var removeFailure = new NpgsqlCommand("""DROP TRIGGER fail_test_block ON classifieds."Publications" """, connection);
        await removeFailure.ExecuteNonQueryAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await BlockAsync(moderator, account)).StatusCode);
        AccountResponse restricted = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        Assert.Equal("Moderation reason", restricted.BlockReason);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{_drafts}/{listing.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync(_drafts, new ListingDraftInput())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.DeleteAsync($"{_drafts}/{listing.Id}?version={listing.Version + 2}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/account/suggestions",
            new { Title = "New story", Description = "Content while blocked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(address)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(otherAddress)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync($"{_blocks}/{account.Id}/unblock", new { restricted.Version })).StatusCode);
        Assert.Null((await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!.BlockReason);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync(_drafts, new ListingDraftInput())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(address)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(otherAddress)).StatusCode);
        Assert.Equal("Blocked", (await owner.GetFromJsonAsync<ListingDraftResponse>($"{_drafts}/{other.Id}"))!.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentFirstApprovalAndAccountBlock_LeavesListingHiddenAsync(bool approvalFirst)
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "Moderator", confirmed: true, role: "Moderator");
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        AccountResponse ownerAccount = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        ListingDraftResponse submitted = await SubmitAsync(owner, admin, "Central");

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using NpgsqlTransaction barrier = await connection.BeginTransactionAsync();
        await using var acquire = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('administration:roles', 0))", connection, barrier);
        await acquire.ExecuteNonQueryAsync();
        Task<HttpResponseMessage> first = approvalFirst ? ApproveAsync(admin, submitted) : BlockAsync(moderator, ownerAccount);
        await WaitForTransactionsAsync(connection, barrier, 1);
        Task<HttpResponseMessage> second = approvalFirst ? BlockAsync(moderator, ownerAccount) : ApproveAsync(admin, submitted);
        await WaitForTransactionsAsync(connection, barrier, 2);
        await barrier.CommitAsync();

        using HttpResponseMessage firstResponse = await first;
        using HttpResponseMessage secondResponse = await second;
        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(approvalFirst ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, secondResponse.StatusCode);
        if (!approvalFirst)
        {
            JsonElement problem = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("content_owner_blocked", problem.GetProperty("code").GetString());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/classifieds/{submitted.Id}")).StatusCode);
    }

    [Fact]
    public async Task PublishedEdits_AppearImmediatelyAndCompareFirstPublicationWithLatestVersionAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true, role: "Moderator");
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        ListingDraftResponse submission = await SubmitAsync(owner, admin, "East");
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(admin, submission)).StatusCode);
        string ownerPath = $"{_drafts}/{submission.Id}";
        string reviewPath = $"{_review}/{submission.Id}";
        string publicPath = $"/api/classifieds/{submission.Id}";
        ListingDraftResponse published = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(ownerPath,
            new UpdateListingDraftRequest { Version = published.Version, Details = published.Details with { Title = "" } })).StatusCode);
        Assert.Equal("Desk", (await visitor.GetFromJsonAsync<PublicListingResponse>(publicPath))!.Title);
        ListingDraftResponse unchanged = await SaveTitleAsync(owner, published, "Desk");
        Assert.Empty((await admin.GetFromJsonAsync<ClassifiedsPage<ListingReviewQueueItem>>(_review))!.Items);

        ListingDraftResponse firstEdit = await SaveTitleAsync(owner, unchanged, "First edit");
        Assert.Equal(published.ExpiresAtUtc, firstEdit.ExpiresAtUtc);
        Assert.Equal("First edit", (await visitor.GetFromJsonAsync<PublicListingResponse>(publicPath))!.Title);
        ListingDraftResponse latest = await SaveTitleAsync(owner, firstEdit, "Latest edit");
        ListingReviewQueueItem pending = Assert.Single((await admin.GetFromJsonAsync<ClassifiedsPage<ListingReviewQueueItem>>(_review))!.Items);
        Assert.Equal(submission.Id, pending.Id);
        Assert.True(pending.PublishedEdit);
        Assert.Equal("Latest edit", pending.Title);
        Assert.Empty((await owner.GetFromJsonAsync<ClassifiedsPage<ListingReviewQueueItem>>(_review))!.Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(reviewPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(reviewPath)).StatusCode);
        ListingReviewPreview comparison = (await admin.GetFromJsonAsync<ListingReviewPreview>(reviewPath))!;
        Assert.Equal("Desk", comparison.Original?.Details.Title);
        Assert.Equal("Latest edit", comparison.Submission.Details.Title);
        Assert.Equal(latest.Version, comparison.Submission.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(admin, firstEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"{_published}/{submission.Id}/block",
            new DecideListingReviewRequest { Version = firstEdit.Version, Reason = "Stale" })).StatusCode);

        ListingDraftResponse renewed = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await RenewAsync(owner, latest));
        Assert.Equal(clock.GetUtcNow().AddDays(7).ToUnixTimeMilliseconds(), renewed.ExpiresAtUtc!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(admin, renewed)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(reviewPath)).StatusCode);
        Assert.Equal(renewed.ExpiresAtUtc, (await visitor.GetFromJsonAsync<PublicListingResponse>(publicPath))!.ExpiresAtUtc);
        ListingDraftResponse reviewed = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
        ListingDraftResponse editedAgain = await SaveTitleAsync(owner, reviewed, "Another edit");
        Assert.Equal("Desk", (await admin.GetFromJsonAsync<ListingReviewPreview>(reviewPath))!.Original?.Details.Title);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{_published}/{submission.Id}/block",
            new DecideListingReviewRequest { Version = editedAgain.Version, Reason = "Needs correction" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(reviewPath)).StatusCode);
        ListingDraftResponse blocked = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
        Assert.Equal("Needs correction", blocked.ReviewReason);
        Assert.Equal(HttpStatusCode.Conflict, (await RenewAsync(owner, blocked)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"{ownerPath}/submit",
            new ListingDraftVersionRequest { Version = blocked.Version })).StatusCode);

        ListingDraftResponse corrected = await SaveTitleAsync(owner, blocked, "Corrected");
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(reviewPath)).StatusCode);
        ListingDraftResponse correction = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(
            $"{ownerPath}/submit", new ListingDraftVersionRequest { Version = corrected.Version }));
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(admin, correction)).StatusCode);
        PublicListingResponse restored = (await visitor.GetFromJsonAsync<PublicListingResponse>(publicPath))!;
        Assert.Equal("Corrected", restored.Title);
        Assert.Equal(renewed.ExpiresAtUtc, restored.ExpiresAtUtc);
    }

    [Fact]
    public async Task PublishedListing_CanBeWithdrawnRelistedRenewedAfterExpiryAndDeletedAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        ListingDraftResponse submission = await SubmitAsync(owner, admin, "West");
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(admin, submission)).StatusCode);
        string ownerPath = $"{_drafts}/{submission.Id}";
        string publicPath = $"/api/classifieds/{submission.Id}";
        ListingDraftResponse withdrawn = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(
            $"{ownerPath}/withdraw", new ListingDraftVersionRequest { Version = submission.Version + 1 }));
        Assert.Equal("Withdrawn", withdrawn.State);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        ListingDraftResponse hiddenEdit = await SaveTitleAsync(owner, withdrawn, "Edited while withdrawn");
        Assert.Equal("Withdrawn", hiddenEdit.State);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        ListingDraftResponse relisted = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await RenewAsync(owner, hiddenEdit));
        Assert.Equal("Published", relisted.State);
        Assert.Equal("Edited while withdrawn", (await visitor.GetFromJsonAsync<PublicListingResponse>(publicPath))!.Title);
        clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        Assert.Equal("Expired", (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!.State);
        ListingDraftResponse renewed = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await RenewAsync(owner, relisted));
        Assert.Equal("Published", renewed.State);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{ownerPath}?version={renewed.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(ownerPath)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<ClassifiedsPage<ListingReviewQueueItem>>(_review))!.Items);

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var history = new NpgsqlCommand("""SELECT "Outcome" FROM classifieds."ReviewDecisions" WHERE "ListingId" = @id""", connection);
        history.Parameters.AddWithValue("id", submission.Id);
        Assert.Equal("Approved", await history.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AccountBlockRacingWithListingWrite_LeavesListingHiddenAsync(bool renew, bool writeFirst)
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        AccountResponse account = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        ListingDraftResponse submitted = await SubmitAsync(owner, admin, "Central");
        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(admin, submitted)).StatusCode);
        string ownerPath = $"{_drafts}/{submitted.Id}";
        ListingDraftResponse listing = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
        if (!renew)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{_published}/{submitted.Id}/block",
                new DecideListingReviewRequest { Version = listing.Version, Reason = "Needs a correction" })).StatusCode);
            ListingDraftResponse blocked = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
            ListingDraftResponse corrected = await SaveTitleAsync(owner, blocked, "Corrected");
            listing = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync($"{ownerPath}/submit",
                new ListingDraftVersionRequest { Version = corrected.Version }));
        }

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using NpgsqlTransaction barrier = await connection.BeginTransactionAsync();
        await using var acquire = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('administration:roles', 0))", connection, barrier);
        await acquire.ExecuteNonQueryAsync();
        Task<HttpResponseMessage> WriteAsync() => renew ? RenewAsync(owner, listing) : ApproveAsync(admin, listing);

        Task<HttpResponseMessage> first = writeFirst ? WriteAsync() : BlockAsync(admin, account);
        await WaitForTransactionsAsync(connection, barrier, 1);
        Task<HttpResponseMessage> second = writeFirst ? BlockAsync(admin, account) : WriteAsync();
        await WaitForTransactionsAsync(connection, barrier, 2);
        await barrier.CommitAsync();

        using HttpResponseMessage firstResponse = await first;
        using HttpResponseMessage secondResponse = await second;
        Assert.Equal(HttpStatusCode.NoContent, (writeFirst ? secondResponse : firstResponse).StatusCode);
        Assert.Equal(writeFirst ? renew ? HttpStatusCode.OK : HttpStatusCode.NoContent : HttpStatusCode.Forbidden,
            (writeFirst ? firstResponse : secondResponse).StatusCode);
        string publicPath = $"/api/classifieds/{submitted.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        AccountResponse blockedAccount = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{_blocks}/{account.Id}/unblock", new { blockedAccount.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(publicPath)).StatusCode);
        if (renew || writeFirst)
        {
            ListingDraftResponse hidden = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
            Assert.Equal("Hidden", hidden.State);
            Assert.Equal(HttpStatusCode.OK, (await RenewAsync(owner, hidden)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(publicPath)).StatusCode);
        }
    }

    private static Task<HttpResponseMessage> RenewAsync(HttpClient owner, ListingDraftResponse listing) =>
        owner.PostAsJsonAsync($"{_drafts}/{listing.Id}/renew", new ListingDraftVersionRequest { Version = listing.Version });

    private static async Task<ListingDraftResponse> SaveTitleAsync(HttpClient owner, ListingDraftResponse listing, string title) =>
        await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PutAsJsonAsync($"{_drafts}/{listing.Id}",
            new UpdateListingDraftRequest { Version = listing.Version, Details = listing.Details with { Title = title } }));

    private static async Task WaitForTransactionsAsync(NpgsqlConnection connection, NpgsqlTransaction barrier, int count)
    {
        await using var waiting = new NpgsqlCommand("""
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """, connection, barrier);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while ((long)(await waiting.ExecuteScalarAsync(timeout.Token))! < count)
            await Task.Delay(20, timeout.Token);
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient staff, ListingDraftResponse listing) =>
        staff.PostAsJsonAsync($"{_review}/{listing.Id}/approve", new ListingDraftVersionRequest { Version = listing.Version });

    private static Task<HttpResponseMessage> BlockAsync(HttpClient staff, AccountResponse target) =>
        staff.PostAsJsonAsync($"{_blocks}/{target.Id}/block", new { target.Version, Reason = "Moderation reason" });

    internal static async Task<ListingDraftResponse> SubmitAsync(HttpClient owner, HttpClient admin, string districtName)
    {
        CatalogEntryResponse category = (await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>("/api/account/classifieds/catalog/categories"))!.Items[0];
        CatalogEntryResponse district = await CitizenSuggestionTests.ReadAsync<CatalogEntryResponse>(
            await admin.PostAsJsonAsync("/api/admin/classifieds/catalog/districts", new SaveClassifiedsCatalogEntryRequest { Name = districtName }));
        var input = new ListingDraftInput
        {
            Title = "Desk",
            Description = "Solid wood.",
            CategoryId = category.Id,
            DistrictId = district.Id,
            Price = 0,
            Phone = "+7 999 000 00 00",
            Email = "private@example.test"
        };
        ListingDraftResponse draft = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(_drafts, input));
        return await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync($"{_drafts}/{draft.Id}/submit",
            new ListingDraftVersionRequest { Version = draft.Version }));
    }
}
