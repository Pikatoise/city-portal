using System.Net;
using System.Net.Http.Json;
using CityPortal.Classifieds;

namespace CityPortal.IntegrationTests;

public sealed class ClassifiedsDraftTests : IClassFixture<RegistrationFixture>
{
    private const string _drafts = "/api/account/classifieds/drafts";
    private const string _catalog = "/api/account/classifieds/catalog";
    private const string _adminCatalog = "/api/admin/classifieds/catalog";
    private readonly RegistrationFixture _fixture;

    public ClassifiedsDraftTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PrivateDraft_EnforcesConfirmationOwnershipAndVersionAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient other = await CitizenSuggestionTests.LoginAsync(factory, database, "Other", confirmed: true);
        using HttpClient unconfirmed = await CitizenSuggestionTests.LoginAsync(factory, database, "Unconfirmed", confirmed: false);
        using HttpClient anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(_drafts)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unconfirmed.PostAsJsonAsync(_drafts, new ListingDraftInput())).StatusCode);
        string csrf = Assert.Single(owner.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN"));
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, new ListingDraftInput())).StatusCode);
        owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, new { Price = 10.25m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, new ListingDraftInput { Price = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, new ListingDraftInput { Price = 1000000000000 })).StatusCode);
        using HttpResponseMessage created = await owner.PostAsJsonAsync(_drafts, new ListingDraftInput());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        ListingDraftResponse draft = Assert.IsType<ListingDraftResponse>(await created.Content.ReadFromJsonAsync<ListingDraftResponse>());
        string path = $"{_drafts}/{draft.Id}";
        Assert.Equal(path, created.Headers.Location?.AbsolutePath);
        Assert.Null(draft.Details.Title);
        Assert.Null(draft.Details.Price);
        Assert.Equal(draft.Id, Assert.Single((await owner.GetFromJsonAsync<ClassifiedsPage<ListingDraftResponse>>(_drafts))!.Items).Id);
        Assert.Empty((await other.GetFromJsonAsync<ClassifiedsPage<ListingDraftResponse>>(_drafts))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);

        var update = new UpdateListingDraftRequest { Version = draft.Version, Details = new ListingDraftInput { Title = "Used book", Price = 0 } };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync(path, update)).StatusCode);
        ListingDraftResponse saved = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PutAsJsonAsync(path, update));
        Assert.Equal(draft.Version + 1, saved.Version);
        Assert.Equal(0L, saved.Details.Price);
        Assert.Equal("Used book", saved.Details.Title);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync(path, update)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"{path}?version={draft.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{path}?version={saved.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task ArchivedCategory_PreservesExistingDraftAndRejectsNewAssignmentAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        await factory.Services.MigrateClassifiedsAsync(CancellationToken.None);
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient administrator = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");

        CatalogEntryResponse[] categories = (await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>($"{_catalog}/categories"))!.Items;
        Assert.Equal(7, categories.Length);
        Assert.Equal(3, categories.Count(item => item.RequiresPrice));
        CatalogEntryResponse category = categories.First(item => item.RequiresPrice);
        var districtInput = new SaveClassifiedsCatalogEntryRequest { Name = "Central" };
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"{_adminCatalog}/districts", districtInput)).StatusCode);
        CatalogEntryResponse district = await CitizenSuggestionTests.ReadAsync<CatalogEntryResponse>(
            await administrator.PostAsJsonAsync($"{_adminCatalog}/districts", districtInput));
        Assert.Equal(district.Id, Assert.Single((await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>($"{_catalog}/districts"))!.Items).Id);

        var input = new ListingDraftInput { CategoryId = category.Id, DistrictId = district.Id };
        ListingDraftResponse draft = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(_drafts, input));
        var archive = new UpdateClassifiedsCatalogEntryRequest
        {
            Version = category.Version,
            Entry = new SaveClassifiedsCatalogEntryRequest { Name = category.Name, RequiresPrice = category.RequiresPrice, Archived = true }
        };
        using HttpResponseMessage archived = await administrator.PutAsJsonAsync($"{_adminCatalog}/categories/{category.Id}", archive);
        archived.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>($"{_catalog}/categories"))!.Items,
            item => item.Id == category.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, input)).StatusCode);
        var update = new UpdateListingDraftRequest { Version = draft.Version, Details = input with { Title = "Retained selection" } };
        ListingDraftResponse saved = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PutAsJsonAsync($"{_drafts}/{draft.Id}", update));
        Assert.Equal(category.Id, saved.Details.CategoryId);
        Assert.Equal(district.Id, saved.Details.DistrictId);
    }
}
