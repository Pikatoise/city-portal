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
