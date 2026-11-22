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
        using HttpClient otherAuthor = await CitizenSuggestionTests.LoginAsync(factory, database, "OtherAuthor", confirmed: true, role: "Author");
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
        string attachmentsPath = $"{_staffPath}/{suggestion.Id}/images";
        ImageResponse[] attachments = (await otherAuthor.GetFromJsonAsync<ImageResponse[]>(attachmentsPath))!;
        Assert.Contains(attachments, image => image.Id == selected.Id && image.State == "Ready");
        Assert.Equal(HttpStatusCode.OK, (await otherAuthor.GetAsync(staffPreview)).StatusCode);
        string importPath = $"{_staffPath}/{suggestion.Id}/import-images";
        Assert.Equal(HttpStatusCode.NotFound, (await otherAuthor.PostAsJsonAsync(importPath, new { imageIds = new[] { selected.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync(staffPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync(articlePreview)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"{_staffPath}/{suggestion.Id}/claim", new { suggestion.Version })).StatusCode);
        StaffSuggestionResponse taken = await AssignmentAsync(author);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(staffPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAuthor.GetAsync(attachmentsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAuthor.GetAsync(staffPreview)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsJsonAsync(importPath, new { imageIds = new[] { foreign.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync(importPath, new { imageIds = new[] { selected.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync(importPath, new { imageIds = new[] { selected.Id } })).StatusCode);
        string draftPath = $"/api/editorial/articles/{taken.ArticleId}";
        ArticleResponse draft = (await author.GetFromJsonAsync<ArticleResponse>(draftPath))!;
        Assert.Equal(selected.Id, Assert.Single(draft.Content.Gallery).MediaId);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(articlePreview)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"{_staffPath}/{suggestion.Id}/return", new { taken.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync(draftPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherAuthor.GetAsync(staffPreview)).StatusCode);
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
        Assert.Equal(HttpStatusCode.NotFound, (await otherAuthor.GetAsync($"{_staffPath}/{expiring.Id}/images/{expiringImage.Id}")).StatusCode);
        // Quartz context is not used by the job.
        await scope.ServiceProvider.GetRequiredService<SuggestionExpiryJob>().Execute(null!, CancellationToken.None);
        await maintenance.CleanupAsync(CancellationToken.None);
        await using var inspected = new NpgsqlCommand("SELECT count(*) FROM media.\"Images\" WHERE \"Id\" = ANY(@ids)", connection);
        inspected.Parameters.AddWithValue("ids", new[] { unused.Id, expiringImage.Id, selected.Id });
        Assert.Equal(1L, await inspected.ExecuteScalarAsync());
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(articlePreview)).StatusCode);
    }

    [Fact]
    public async Task Assignment_RollsBackDraftHistoryAndMediaWhenSuggestionWriteFailsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient resident = await CitizenSuggestionTests.LoginAsync(factory, database, "Resident", confirmed: true);
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "Author", confirmed: true, role: "Author");
        ImageResponse image = await UploadAsync(resident);
        SuggestionResponse suggestion = await CitizenSuggestionTests.ReadAsync<SuggestionResponse>(
            await resident.PostAsJsonAsync(_residentPath, new SuggestionInput
            {
                Title = "Park cleanup",
                Description = "Photos from the resident.",
                ImageIds = [image.Id]
            }));
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
        string residentPath = $"{_residentPath}/{suggestion.Id}";
        string staffPath = $"{_staffPath}/{suggestion.Id}";
        SuggestionResponse queued = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();

        // Fail the suggestion write after News has saved the new draft.
        await ExecuteAsync(connection, """ALTER TABLE citizen_submissions."Suggestions" ADD CONSTRAINT reject_claim CHECK ("ArticleId" IS NULL)""");
        using HttpResponseMessage failedClaim = await author.PostAsJsonAsync(staffPath + "/claim", new { queued.Version });
        Assert.Equal(HttpStatusCode.InternalServerError, failedClaim.StatusCode);
        SuggestionResponse stillQueued = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;
        Assert.Equal("Queued", stillQueued.Status);
        Assert.Equal(queued.Version, stillQueued.Version);
        Assert.Equal(queued.ExpiresAtUtc, stillQueued.ExpiresAtUtc);
        Assert.True(stillQueued.CanEdit);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"{staffPath}/images/{image.Id}")).StatusCode);
        await using var newsRows = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM news."Articles") + (SELECT count(*) FROM news."Revisions") + (SELECT count(*) FROM news."History")
            """, connection);
        Assert.Equal(0L, await newsRows.ExecuteScalarAsync());

        await ExecuteAsync(connection, """ALTER TABLE citizen_submissions."Suggestions" DROP CONSTRAINT reject_claim""");
        using HttpResponseMessage claimed = await author.PostAsJsonAsync(staffPath + "/claim", new { queued.Version });
        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
        StaffSuggestionResponse assigned = await AssignmentAsync(author);
        using HttpResponseMessage imported = await author.PostAsJsonAsync(staffPath + "/import-images", new { imageIds = new[] { image.Id } });
        Assert.Equal(HttpStatusCode.NoContent, imported.StatusCode);
        string draftPath = $"/api/editorial/articles/{assigned.ArticleId}";
        string imagePath = $"/api/media/images/{image.Id}/1280";
        ArticleResponse draft = (await author.GetFromJsonAsync<ArticleResponse>(draftPath))!;
        await using var history = new NpgsqlCommand("""SELECT count(*) FROM news."History" WHERE "ArticleId" = @id""", connection);
        history.Parameters.AddWithValue("id", draft.Id);
        long historyCount = Assert.IsType<long>(await history.ExecuteScalarAsync());
        Assert.True(historyCount > 0);

        // Fail the observer after News has saved deletion history and changed Media access.
        await ExecuteAsync(connection, """ALTER TABLE citizen_submissions."Suggestions" ADD CONSTRAINT reject_return CHECK ("ArticleId" IS NOT NULL)""");
        using HttpResponseMessage failedReturn = await author.PostAsJsonAsync(staffPath + "/return", new { assigned.Version });
        Assert.Equal(HttpStatusCode.InternalServerError, failedReturn.StatusCode);
        Assert.Equal(assigned, await AssignmentAsync(author));
        ArticleResponse retained = (await author.GetFromJsonAsync<ArticleResponse>(draftPath))!;
        Assert.Equal(draft.Version, retained.Version);
        Assert.Equal(image.Id, Assert.Single(retained.Content.Gallery).MediaId);
        Assert.Equal(historyCount, await history.ExecuteScalarAsync());
        using HttpResponseMessage media = await author.GetAsync(imagePath);
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);

        await ExecuteAsync(connection, """ALTER TABLE citizen_submissions."Suggestions" DROP CONSTRAINT reject_return""");
        using HttpResponseMessage returned = await author.PostAsJsonAsync(staffPath + "/return", new { assigned.Version });
        Assert.Equal(HttpStatusCode.NoContent, returned.StatusCode);
        Assert.Equal(historyCount + 1, await history.ExecuteScalarAsync());
        using HttpResponseMessage deletedDraft = await author.GetAsync(draftPath);
        using HttpResponseMessage removedMedia = await author.GetAsync(imagePath);
        Assert.Equal(HttpStatusCode.NotFound, deletedDraft.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removedMedia.StatusCode);
        SuggestionResponse returnedSuggestion = (await resident.GetFromJsonAsync<SuggestionResponse>(residentPath))!;
        Assert.Equal(assigned.Version + 1, returnedSuggestion.Version);
        Assert.False(returnedSuggestion.CanEdit);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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
