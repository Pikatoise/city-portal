using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CityPortal.CitizenSubmissions;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class CitizenSuggestionMediaTests : IClassFixture<MediaFixture>
{
    private const string _residentPath = "/api/account/suggestions";
    private const string _staffPath = "/api/staff/suggestions";
    private readonly MediaFixture _fixture;

    public CitizenSuggestionMediaTests(MediaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SuggestionImages_ProtectAccessAndRetainOnlyLinkedArticleMaterialsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient resident = await CitizenSuggestionTests.LoginAsync(factory, database, "Resident", confirmed: true);
        using HttpClient otherResident = await CitizenSuggestionTests.LoginAsync(factory, database, "Neighbour", confirmed: true);
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "Author", confirmed: true, role: "Author");
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "Moderator", confirmed: true, role: "Moderator");
        ImageResponse selected = await UploadAsync(resident);
        ImageResponse unused = await UploadAsync(resident);
        ImageResponse foreign = await UploadAsync(otherResident);
        var input = new SuggestionInput { Title = "Park cleanup", Description = "Cleanup photos from the resident." };
        using HttpResponseMessage foreignAttachment = await resident.PostAsJsonAsync(_residentPath, input with { ImageIds = [foreign.Id] });
        Assert.Equal(HttpStatusCode.NotFound, foreignAttachment.StatusCode);
        using HttpResponseMessage excessive = await resident.PostAsJsonAsync(_residentPath, input with { ImageIds = [selected.Id, unused.Id, foreign.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()] });
        Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode);
        Assert.Empty((await resident.GetFromJsonAsync<SuggestionPage>(_residentPath))!.Items);

        SuggestionResponse suggestion = await CitizenSuggestionTests.ReadAsync<SuggestionResponse>(
            await resident.PostAsJsonAsync(_residentPath, input with { ImageIds = [selected.Id, unused.Id] }));
        Assert.Equal(2, suggestion.Images.Length);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MediaMaintenance maintenance = scope.ServiceProvider.GetRequiredService<MediaMaintenance>();
        await maintenance.ProcessPendingAsync(CancellationToken.None);
        string residentPreview = $"{_residentPath}/{suggestion.Id}/images/{selected.Id}";
        string staffPreview = $"{_staffPath}/{suggestion.Id}/images/{selected.Id}";
        string articlePreview = $"/api/media/images/{selected.Id}/1280";
        Assert.Equal(HttpStatusCode.OK, (await resident.GetAsync(residentPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherResident.GetAsync(residentPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync(staffPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync(staffPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync(articlePreview)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"{_staffPath}/{suggestion.Id}/claim", new { suggestion.Version })).StatusCode);
        StaffSuggestionResponse taken = await AssignmentAsync(author);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(staffPreview)).StatusCode);
        string importPath = $"{_staffPath}/{suggestion.Id}/import-images";
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsJsonAsync(importPath, new { imageIds = new[] { foreign.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync(importPath, new { imageIds = new[] { selected.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync(importPath, new { imageIds = new[] { selected.Id } })).StatusCode);
        string draftPath = $"/api/editorial/articles/{taken.ArticleId}";
        ArticleResponse draft = (await author.GetFromJsonAsync<ArticleResponse>(draftPath))!;
        Assert.Equal(selected.Id, Assert.Single(draft.Content.Gallery).MediaId);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(articlePreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"{_staffPath}/{suggestion.Id}/return", new { taken.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync(draftPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync(staffPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync(articlePreview)).StatusCode);

        SuggestionResponse queued = (await resident.GetFromJsonAsync<SuggestionResponse>($"{_residentPath}/{suggestion.Id}"))!;
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"{_staffPath}/{suggestion.Id}/claim", new { queued.Version })).StatusCode);
        taken = await AssignmentAsync(author);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync(importPath, new { imageIds = new[] { selected.Id, unused.Id } })).StatusCode);
        string secondDraftPath = $"/api/editorial/articles/{taken.ArticleId}";
        ArticleResponse secondDraft = (await author.GetFromJsonAsync<ArticleResponse>(secondDraftPath))!;
        ArticleContent reduced = secondDraft.Content with { Gallery = [.. secondDraft.Content.Gallery.Where(image => image.MediaId == selected.Id)] };
        using HttpResponseMessage saved = await author.PatchAsJsonAsync(secondDraftPath, new { secondDraft.Version, content = reduced });
        saved.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync($"{_staffPath}/{suggestion.Id}/delete",
            new { taken.Version, reason = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync(residentPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.GetAsync(staffPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(articlePreview)).StatusCode);

        ImageResponse expiringImage = await UploadAsync(resident);
        SuggestionResponse expiring = await CitizenSuggestionTests.ReadAsync<SuggestionResponse>(
            await resident.PostAsJsonAsync(_residentPath, input with { ImageIds = [expiringImage.Id] }));
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var expire = new NpgsqlCommand("UPDATE citizen_submissions.\"Suggestions\" SET \"ExpiresAtUtc\" = now() - interval '1 second' WHERE \"Id\" = @id", connection);
        expire.Parameters.AddWithValue("id", expiring.Id);
        Assert.Equal(1, await expire.ExecuteNonQueryAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync($"{_residentPath}/{expiring.Id}")).StatusCode);
        // Quartz context is not used by the job.
        await scope.ServiceProvider.GetRequiredService<SuggestionExpiryJob>().Execute(null!, CancellationToken.None);
        await maintenance.CleanupAsync(CancellationToken.None);
        await using var inspected = new NpgsqlCommand("SELECT count(*) FROM media.\"Images\" WHERE \"Id\" = ANY(@ids)", connection);
        inspected.Parameters.AddWithValue("ids", new[] { unused.Id, expiringImage.Id, selected.Id });
        Assert.Equal(1L, await inspected.ExecuteScalarAsync());
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(articlePreview)).StatusCode);
    }

    private static async Task<StaffSuggestionResponse> AssignmentAsync(HttpClient author)
    {
        StaffSuggestionPage page = (await author.GetFromJsonAsync<StaffSuggestionPage>($"{_staffPath}/queue?mine=true"))!;
        return Assert.Single(page.Items);
    }

    private static async Task<ImageResponse> UploadAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(MediaProcessorTests.Image());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "park.png");
        using HttpResponseMessage response = await client.PostAsync($"{_residentPath}/uploads", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.IsType<ImageResponse>(await response.Content.ReadFromJsonAsync<ImageResponse>());
    }
}
