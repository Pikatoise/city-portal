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

    private static async Task<ListingDraftResponse> SubmitAsync(HttpClient owner, HttpClient admin, string districtName)
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
