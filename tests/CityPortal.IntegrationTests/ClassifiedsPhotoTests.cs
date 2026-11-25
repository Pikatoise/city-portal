using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CityPortal.Classifieds;
using CityPortal.Media;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

[Collection("Media upload slot")]
public sealed class ClassifiedsPhotoTests : IClassFixture<MediaFixture>
{
    private const string _account = "/api/account/classifieds";
    private const string _drafts = _account + "/drafts";
    private readonly MediaFixture _fixture;

    public ClassifiedsPhotoTests(MediaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DraftPhotos_EnforceLimitOwnershipOrderAndCleanupAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "PhotoOwner", confirmed: true, role: "Author");
        using HttpClient other = await CitizenSuggestionTests.LoginAsync(factory, database, "Other", confirmed: true);
        using HttpClient anonymous = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var photos = new ListingPhotoInput[5];
        for (int index = 0; index < photos.Length; index++)
        {
            ImageResponse image = await UploadAsync(owner);
            photos[index] = new ListingPhotoInput { ImageId = image.Id, AlternativeText = $"Photo {index + 1}" };
        }
        ImageResponse foreign = await UploadAsync(other);
        var draftInput = new ListingDraftInput { Title = "Listing with private photos", Photos = photos };
        ListingDraftInput tooMany = draftInput with { Photos = [.. photos, new ListingPhotoInput { ImageId = Guid.NewGuid(), AlternativeText = "Sixth" }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, tooMany)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, draftInput with { Photos = [photos[0], photos[0]] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(_drafts, draftInput with { Photos = [photos[0] with { AlternativeText = " " }] })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(_drafts,
            draftInput with { Photos = [photos[0], photos[1] with { ImageId = foreign.Id }] })).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<ClassifiedsPage<ListingDraftResponse>>(_drafts))!.Items);

        ListingDraftResponse draft = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(_drafts, draftInput));
        Assert.Equal(photos.Select(photo => photo.ImageId), draft.Photos.Select(photo => photo.Image.Id));
        Assert.Equal(photos.Select(photo => photo.AlternativeText), draft.Photos.Select(photo => photo.AlternativeText));
        Assert.All(draft.Photos, photo => Assert.Equal("Processing", photo.Image.State));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(_drafts, draftInput)).StatusCode);
        string itemPath = $"{_drafts}/{draft.Id}";
        string preview = $"{itemPath}/images/{photos[0].ImageId}";
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(preview)).StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MediaMaintenance maintenance = scope.ServiceProvider.GetRequiredService<MediaMaintenance>();
        await maintenance.ProcessPendingAsync(CancellationToken.None);
        using HttpResponseMessage opened = await owner.GetAsync(preview);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("image/webp", opened.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(preview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(preview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/media/images/{photos[0].ImageId}/1280")).StatusCode);

        var rename = new UpdateListingDraftRequest { Version = draft.Version, Details = new ListingDraftInput { Title = "Renamed" } };
        ListingDraftResponse renamed = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PutAsJsonAsync(itemPath, rename));
        Assert.Equal(photos.Select(photo => photo.ImageId), renamed.Photos.Select(photo => photo.Image.Id));
        Assert.All(renamed.Photos, photo => Assert.Equal("Ready", photo.Image.State));

        ListingPhotoInput[] remaining = photos.Skip(1).Reverse().ToArray();
        var reorder = new UpdateListingDraftRequest { Version = renamed.Version, Details = draftInput with { Photos = remaining } };
        ListingDraftResponse updated = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PutAsJsonAsync(itemPath, reorder));
        Assert.Equal(remaining.Select(photo => photo.ImageId), updated.Photos.Select(photo => photo.Image.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(preview)).StatusCode);
        await maintenance.CleanupAsync(CancellationToken.None);
        await AssertMediaCountAsync(database, photos, 4);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{itemPath}?version={updated.Version}")).StatusCode);
        await maintenance.CleanupAsync(CancellationToken.None);
        await AssertMediaCountAsync(database, photos, 0);
    }

    [Fact]
    public async Task StaffPhoto_RequiresCurrentPendingVersionAndReviewPermissionAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "PhotoOwner", confirmed: true, role: "Moderator");
        using HttpClient reviewer = await CitizenSuggestionTests.LoginAsync(factory, database, "Reviewer", confirmed: true, role: "Administrator");
        using HttpClient other = await CitizenSuggestionTests.LoginAsync(factory, database, "Other", confirmed: true);
        ImageResponse photo = await UploadAsync(owner);
        CatalogEntryResponse category = (await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>(_account + "/catalog/categories"))!.Items[0];
        CatalogEntryResponse district = await CitizenSuggestionTests.ReadAsync<CatalogEntryResponse>(
            await reviewer.PostAsJsonAsync("/api/admin/classifieds/catalog/districts", new SaveClassifiedsCatalogEntryRequest { Name = "Central" }));
        var details = new ListingDraftInput
        {
            Title = "Desk",
            Description = "Solid wood",
            CategoryId = category.Id,
            DistrictId = district.Id,
            Email = "owner@example.test",
            Price = 0,
            Photos = [new ListingPhotoInput { ImageId = photo.Id, AlternativeText = "Wooden desk" }]
        };
        ListingDraftResponse draft = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(_drafts, details));
        string ownerPath = $"{_drafts}/{draft.Id}";
        string staffPath = $"/api/staff/classifieds/review/{draft.Id}";
        string staffPhoto = $"{staffPath}/images/{photo.Id}";
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"{staffPhoto}?version={draft.Version}")).StatusCode);

        ListingDraftResponse submitted = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PostAsJsonAsync(ownerPath + "/submit", new ListingDraftVersionRequest { Version = draft.Version }));
        string pendingPhoto = $"{staffPhoto}?version={submitted.Version}";
        ListingReviewPreview preview = (await reviewer.GetFromJsonAsync<ListingReviewPreview>(staffPath))!;
        Assert.Equal("Wooden desk", Assert.Single(preview.Submission.Photos).AlternativeText);
        using HttpResponseMessage opened = await reviewer.GetAsync(pendingPhoto);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("image/webp", opened.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(pendingPhoto)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(pendingPhoto)).StatusCode);

        ListingDraftResponse recalled = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PostAsJsonAsync(ownerPath + "/recall", new ListingDraftVersionRequest { Version = submitted.Version }));
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync(pendingPhoto)).StatusCode);
        ListingDraftResponse resubmitted = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PostAsJsonAsync(ownerPath + "/submit", new ListingDraftVersionRequest { Version = recalled.Version }));
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync(pendingPhoto)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsJsonAsync(staffPath + "/reject",
            new DecideListingReviewRequest { Version = resubmitted.Version, Reason = "Final decision" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync($"{staffPhoto}?version={resubmitted.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{ownerPath}/images/{photo.Id}")).StatusCode);
    }

    [Fact]
    public async Task PublishedPhoto_IsAvailableOnlyWhileTheListingIsPublicAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient visitor = factory.CreateClient();
        ImageResponse uploaded = await UploadAsync(owner);
        CatalogEntryResponse category = (await owner.GetFromJsonAsync<ClassifiedsPage<CatalogEntryResponse>>(_account + "/catalog/categories"))!.Items[0];
        CatalogEntryResponse district = await CitizenSuggestionTests.ReadAsync<CatalogEntryResponse>(
            await admin.PostAsJsonAsync("/api/admin/classifieds/catalog/districts", new SaveClassifiedsCatalogEntryRequest { Name = "Central" }));
        ListingDraftResponse draft = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(_drafts, new ListingDraftInput
        {
            Title = "Desk",
            Description = "Solid wood.",
            CategoryId = category.Id,
            DistrictId = district.Id,
            Phone = "+7 999 000 00 00",
            Price = 0,
            Photos = [new ListingPhotoInput { ImageId = uploaded.Id, AlternativeText = "Wooden desk" }]
        }));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
        string publicPath = $"/api/classifieds/{draft.Id}";
        string photoPath = $"{publicPath}/images/{uploaded.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(photoPath)).StatusCode);
        ListingDraftResponse submitted = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(await owner.PostAsJsonAsync(
            $"{_drafts}/{draft.Id}/submit", new ListingDraftVersionRequest { Version = draft.Version }));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/staff/classifieds/review/{draft.Id}/approve",
            new ListingDraftVersionRequest { Version = submitted.Version })).StatusCode);

        PublicListingResponse published = (await visitor.GetFromJsonAsync<PublicListingResponse>(publicPath))!;
        PublishedListingPhoto photo = Assert.Single(published.Photos);
        Assert.Equal(uploaded.Id, photo.Id);
        Assert.Equal("Wooden desk", photo.AlternativeText);
        using HttpResponseMessage opened = await visitor.GetAsync(photoPath);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("image/webp", opened.Content.Headers.ContentType?.MediaType);
        Assert.True(opened.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"{publicPath}/images/{Guid.NewGuid()}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/staff/classifieds/published/{draft.Id}/block",
            new DecideListingReviewRequest { Version = submitted.Version + 1, Reason = "Prohibited listing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(photoPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"{_drafts}/{draft.Id}/images/{uploaded.Id}")).StatusCode);
    }

    private static async Task AssertMediaCountAsync(string database, ListingPhotoInput[] photos, long expected)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM media.\"Images\" WHERE \"Id\" = ANY(@ids)", connection);
        command.Parameters.AddWithValue("ids", photos.Select(photo => photo.ImageId).ToArray());
        Assert.Equal(expected, await command.ExecuteScalarAsync());
    }

    private static async Task<ImageResponse> UploadAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(MediaProcessorTests.Image());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "listing.png");
        using HttpResponseMessage response = await client.PostAsync(_account + "/uploads", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.IsType<ImageResponse>(await response.Content.ReadFromJsonAsync<ImageResponse>());
    }
}
