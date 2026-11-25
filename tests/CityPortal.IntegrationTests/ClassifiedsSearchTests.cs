using System.Net;
using System.Net.Http.Json;
using CityPortal.Classifieds;
using CityPortal.Identity;

namespace CityPortal.IntegrationTests;

public sealed class ClassifiedsSearchTests : IClassFixture<RegistrationFixture>
{
    private const string _catalog = "/api/classifieds";
    private const string _drafts = "/api/account/classifieds/drafts";
    private readonly RegistrationFixture _fixture;

    public ClassifiedsSearchTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Catalog_CombinesCurrentTextWithFiltersAndSharedVisibilityAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        ListingDraftInput input = await DetailsAsync(owner, admin);
        ListingDraftResponse first = await PublishAsync(owner, admin, input with { Title = "Уход за кошками", Price = 500 });
        clock.Advance(TimeSpan.FromHours(1));
        ListingDraftResponse second = await PublishAsync(owner, admin, input with { Title = "Передержка животных", Description = "Присмотрю за кошкой." });
        await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(_drafts, input with { Title = "Кошки в черновике" }));
        string search = "?query=" + Uri.EscapeDataString("кошки");
        Assert.Equal(new[] { second.Id, first.Id }, (await BrowseAsync(visitor)).Items.Select(item => item.Id));
        Assert.Equal(new[] { first.Id, second.Id }, (await BrowseAsync(visitor, search)).Items.Select(item => item.Id));
        string combined = search + $"&categoryId={input.CategoryId}&minPrice=500&maxPrice=500";
        Assert.Equal(first.Id, Assert.Single((await BrowseAsync(visitor, combined + $"&districtId={input.DistrictId}")).Items).Id);
        Assert.Empty((await BrowseAsync(visitor, combined + $"&districtId={Guid.NewGuid()}")).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync(_catalog + "?minPrice=2&maxPrice=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync(_catalog + "?minPrice=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync(_catalog + "?page=0")).StatusCode);
        using HttpResponseMessage publicResponse = await visitor.GetAsync(_catalog);
        Assert.True(publicResponse.Headers.CacheControl?.NoStore);
        PublicListingFilters filters = (await visitor.GetFromJsonAsync<PublicListingFilters>(_catalog + "/filters"))!;
        Assert.Contains(filters.Categories, option => option.Id == input.CategoryId);
        Assert.Equal(input.DistrictId, Assert.Single(filters.Districts).Id);

        ListingDraftResponse edited = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PutAsJsonAsync(
            $"{_drafts}/{first.Id}", new UpdateListingDraftRequest { Version = first.Version, Details = input with { Title = "Прокат велосипедов", Price = 500 } }));
        Assert.Equal(second.Id, Assert.Single((await BrowseAsync(visitor, search)).Items).Id);
        Assert.Equal(first.Id, Assert.Single((await BrowseAsync(visitor, "?query=" + Uri.EscapeDataString("велосипеды"))).Items).Id);
        ListingDraftResponse withdrawn = await OwnerActionAsync(owner, edited, "withdraw");
        Assert.Equal(second.Id, Assert.Single((await BrowseAsync(visitor)).Items).Id);
        ListingDraftResponse renewed = await OwnerActionAsync(owner, withdrawn, "renew");
        Assert.Equal(new[] { second.Id, first.Id }, (await BrowseAsync(visitor)).Items.Select(item => item.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(
            $"/api/staff/classifieds/published/{second.Id}/block",
            new DecideListingReviewRequest { Version = second.Version, Reason = "Prohibited listing" })).StatusCode);
        Assert.Equal(first.Id, Assert.Single((await BrowseAsync(visitor)).Items).Id);
        AccountResponse account = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        string blockPath = $"/api/staff/account-blocks/{account.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(blockPath + "/block", new { account.Version, Reason = "Moderation" })).StatusCode);
        Assert.Empty((await BrowseAsync(visitor)).Items);
        account = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(blockPath + "/unblock", new { account.Version })).StatusCode);
        Assert.Empty((await BrowseAsync(visitor)).Items);

        ListingDraftResponse hidden = (await owner.GetFromJsonAsync<ListingDraftResponse>($"{_drafts}/{renewed.Id}"))!;
        ListingDraftResponse relisted = await OwnerActionAsync(owner, hidden, "renew");
        DateTimeOffset expiry = relisted.ExpiresAtUtc!.Value;
        clock.Advance(expiry - clock.GetUtcNow() - TimeSpan.FromMilliseconds(1));
        Assert.Equal(first.Id, Assert.Single((await BrowseAsync(visitor)).Items).Id);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Empty((await BrowseAsync(visitor)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"{_catalog}/{first.Id}")).StatusCode);
    }

    [Fact]
    public async Task Catalog_PagesTwentyListingsInFirstPublicationOrderAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        ListingDraftInput input = await DetailsAsync(owner, admin);
        var ids = new List<Guid>();
        for (int index = 0; index < 21; index++)
        {
            ListingDraftResponse listing = await PublishAsync(owner, admin, input with { Title = $"Listing {index}" });
            ids.Insert(0, listing.Id);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        ClassifiedsPage<PublicListingSummary> first = await BrowseAsync(visitor);
        Assert.Equal(ids.Take(20), first.Items.Select(item => item.Id));
        Assert.True(first.HasNextPage);
        ClassifiedsPage<PublicListingSummary> second = await BrowseAsync(visitor, "?page=2");
        Assert.Equal(ids[20], Assert.Single(second.Items).Id);
        Assert.False(second.HasNextPage);
    }

    private static async Task<ListingDraftInput> DetailsAsync(HttpClient owner, HttpClient admin)
    {
        ClassifiedsPage<CatalogEntryResponse>? categories = await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>("/api/account/classifieds/catalog/categories");
        CatalogEntryResponse category = categories!.Items.First(entry => !entry.RequiresPrice);
        CatalogEntryResponse district = await CitizenSuggestionTests.ReadAsync<CatalogEntryResponse>(
            await admin.PostAsJsonAsync("/api/admin/classifieds/catalog/districts", new SaveClassifiedsCatalogEntryRequest { Name = "Central" }));
        return new ListingDraftInput
        {
            Description = "Details",
            CategoryId = category.Id,
            DistrictId = district.Id,
            Phone = "+7 999 000 00 00",
            Email = "private@example.test"
        };
    }

    private static async Task<ListingDraftResponse> PublishAsync(HttpClient owner, HttpClient admin, ListingDraftInput input)
    {
        ListingDraftResponse draft = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(_drafts, input));
        ListingDraftResponse submitted = await OwnerActionAsync(owner, draft, "submit");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/staff/classifieds/review/{draft.Id}/approve",
            new ListingDraftVersionRequest { Version = submitted.Version })).StatusCode);
        return (await owner.GetFromJsonAsync<ListingDraftResponse>($"{_drafts}/{draft.Id}"))!;
    }

    private static async Task<ListingDraftResponse> OwnerActionAsync(HttpClient owner, ListingDraftResponse draft, string action) =>
        await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(
            $"{_drafts}/{draft.Id}/{action}", new ListingDraftVersionRequest { Version = draft.Version }));

    private static async Task<ClassifiedsPage<PublicListingSummary>> BrowseAsync(HttpClient visitor, string query = "") =>
        (await visitor.GetFromJsonAsync<ClassifiedsPage<PublicListingSummary>>(_catalog + query))!;
}
