using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task Submission_ValidatesFieldsFreezesDraftAndAllowsVersionedRecallAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Submitter", confirmed: true);
        using HttpClient other = await CitizenSuggestionTests.LoginAsync(factory, database, "Other", confirmed: true);
        using HttpClient administrator = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        CatalogEntryResponse category = (await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>($"{_catalog}/categories"))!
            .Items.First(entry => entry.RequiresPrice);
        CatalogEntryResponse district = await CitizenSuggestionTests.ReadAsync<CatalogEntryResponse>(
            await administrator.PostAsJsonAsync($"{_adminCatalog}/districts", new SaveClassifiedsCatalogEntryRequest { Name = "Central" }));
        ListingDraftResponse draft = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PostAsJsonAsync(_drafts, new ListingDraftInput()));
        string path = $"{_drafts}/{draft.Id}";
        var version = new ListingDraftVersionRequest { Version = draft.Version };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(path + "/submit", version)).StatusCode);

        using HttpResponseMessage incomplete = await owner.PostAsJsonAsync(path + "/submit", version);
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        JsonElement invalidDraft = (await incomplete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(new[] { "categoryId", "description", "districtId", "phone", "title" },
            invalidDraft.EnumerateObject().Select(field => field.Name).Order().ToArray());
        ListingDraftResponse unchanged = (await owner.GetFromJsonAsync<ListingDraftResponse>(path))!;
        Assert.Equal(draft.Version, unchanged.Version);
        Assert.Equal("Draft", unchanged.State);

        var details = new ListingDraftInput
        {
            Title = "Desk",
            Description = "Solid wood.",
            CategoryId = category.Id,
            DistrictId = district.Id,
            Email = "invalid"
        };
        ListingDraftResponse saved = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PutAsJsonAsync(path, new UpdateListingDraftRequest { Version = draft.Version, Details = details }));
        using HttpResponseMessage invalid = await owner.PostAsJsonAsync(path + "/submit", version with { Version = saved.Version });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        JsonElement invalidFields = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(invalidFields.TryGetProperty("price", out _));
        Assert.True(invalidFields.TryGetProperty("email", out _));

        var update = new UpdateListingDraftRequest { Version = saved.Version, Details = details with { Price = 0, Email = "seller@example.test" } };
        ListingDraftResponse complete = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PutAsJsonAsync(path, update));
        version = version with { Version = complete.Version };
        HttpResponseMessage[] attempts = await Task.WhenAll(owner.PostAsJsonAsync(path + "/submit", version), owner.PostAsJsonAsync(path + "/submit", version));
        using HttpResponseMessage winner = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        using HttpResponseMessage stale = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        ListingDraftResponse submitted = Assert.IsType<ListingDraftResponse>(await winner.Content.ReadFromJsonAsync<ListingDraftResponse>());
        Assert.Equal("InReview", submitted.State);
        Assert.Equal(complete.Version + 1, submitted.Version);
        Assert.Equal(0L, submitted.Details.Price);
        Assert.Equal("InReview", Assert.Single((await owner.GetFromJsonAsync<ClassifiedsPage<ListingDraftResponse>>(_drafts))!.Items).State);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync(path, update with { Version = submitted.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"{path}?version={submitted.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path + "/recall", version)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(path + "/recall", version with { Version = submitted.Version })).StatusCode);

        ListingDraftResponse recalled = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PostAsJsonAsync(path + "/recall", version with { Version = submitted.Version }));
        Assert.Equal("Draft", recalled.State);
        Assert.Equal(submitted.Version + 1, recalled.Version);
        Assert.Equal(submitted.Details, recalled.Details);
        ListingDraftResponse editing = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PutAsJsonAsync(path, update with { Version = recalled.Version, Details = update.Details with { Title = "Revised desk" } }));
        Assert.Equal("Revised desk", editing.Details.Title);
    }
}
