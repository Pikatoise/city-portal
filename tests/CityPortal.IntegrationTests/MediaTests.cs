using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class MediaTests : IClassFixture<MediaFixture>
{
    private readonly MediaFixture _fixture;
    private const string _password = "MediaPassword1!";

    public MediaTests(MediaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Templates_UnicodeLimits_NormalizeNamesAndCountScalarsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient moderator) = await ActorAsync(factory, database, "Moderator");
        using (owner)
        using (moderator)
        {
            ImageResponse image = await UploadAsync(owner);
            await ProcessAsync(factory);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/media/templates", new { imageId = image.Id, name = new string('a', 101) })).StatusCode);
            string decomposed = string.Concat(Enumerable.Repeat("e\u0301", 100));
            using HttpResponseMessage created = await owner.PostAsJsonAsync("/api/media/templates", new { imageId = image.Id, name = decomposed });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            TemplateResponse template = (await created.Content.ReadFromJsonAsync<TemplateResponse>())!;
            Assert.Equal(new string('\u00e9', 100), template.Name);
            template = await ActionAsync(owner, template, "submit");
            string reason = string.Concat(Enumerable.Repeat("\U0001F331", 2000));
            Assert.Equal(HttpStatusCode.BadRequest, (await moderator.PostAsJsonAsync($"/api/media/templates/{template.Id}/reject", new { template.Version, reason = reason + "a" })).StatusCode);
            template = await ActionAsync(moderator, template, "reject", reason);
            Assert.Equal(reason, template.Reason);
        }
    }

    [Fact]
    public async Task TemplateNotifications_DecisionsAndAffectedCurrentAuthors_QueueOnceWithoutPrivateContentAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid ownerId, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid authorId, HttpClient author) = await ActorAsync(factory, database, "Author");
        (Guid formerId, HttpClient former) = await ActorAsync(factory, database, "Author");
        (_, HttpClient moderator) = await ActorAsync(factory, database, "Moderator");
        using (owner)
        using (author)
        using (former)
        using (moderator)
        {
            ImageResponse image = await UploadAsync(owner);
            await ProcessAsync(factory);
            TemplateResponse template = await CreateTemplateAsync(owner, image.Id);
            template = await ActionAsync(owner, template, "submit");
            template = await ActionAsync(moderator, template, "approve");
            AccountNotificationResponse approved = Assert.Single((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.Equal("template.approved", approved.Type);
            Assert.Equal(template.Id, approved.ResourceId);
            Assert.Empty((await moderator.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            await ReferencesAsync(factory, database, Guid.NewGuid(), authorId, null, false, [image.Id]);
            await ReferencesAsync(factory, database, Guid.NewGuid(), authorId, null, true, [image.Id]);
            await ReferencesAsync(factory, database, Guid.NewGuid(), formerId, null, false, [image.Id]);
            await SqlAsync(database, "UPDATE media.\"References\" SET \"Current\" = false WHERE \"AuthorId\" = @id", formerId);
            template = await ActionAsync(moderator, template, "block", "Private moderation reason");
            AccountNotificationResponse affected = Assert.Single((await author.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.Equal("image.blocked", affected.Type);
            Assert.Equal(image.Id, affected.ResourceId);
            Assert.DoesNotContain("Private moderation reason", affected.Message);
            Assert.Empty((await former.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.Equal(new[] { "template.approved", "template.blocked" }, (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items.Select(item => item.Type).Order().ToArray());
            Assert.Equal(HttpStatusCode.Conflict, (await moderator.PostAsJsonAsync($"/api/media/templates/{template.Id}/block", new { template.Version, reason = "duplicate" })).StatusCode);
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            {
                TemplateNotifications dispatch = scope.ServiceProvider.GetRequiredService<TemplateNotifications>();
                await dispatch.DispatchPendingAsync(CancellationToken.None);
                await dispatch.DispatchPendingAsync(CancellationToken.None);
            }
            Assert.Single((await author.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            template = await ActionAsync(moderator, template, "unblock", "Restored");
            Assert.Equal(new[] { "image.blocked", "image.unblocked" }, (await author.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items.Select(item => item.Type).Order().ToArray());
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/account/notifications/{approved.Id}")).StatusCode);
            await SqlAsync(database, "UPDATE media.\"Events\" SET \"NotificationDispatchedAtUtc\" = NULL WHERE \"TemplateId\" = @id", template.Id);
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<TemplateNotifications>().DispatchPendingAsync(CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<TemplateNotifications>().DispatchPendingAsync(CancellationToken.None);
            }
            Assert.Equal(2, (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Total);
            MediaPage<TemplateHistoryResponse> history = (await owner.GetFromJsonAsync<MediaPage<TemplateHistoryResponse>>($"/api/media/templates/{template.Id}/history"))!;
            Assert.Contains(history.Items, item => item.Action == "approve");
        }
    }

    [Fact]
    public async Task TemplateNotifications_QueueFailure_RollsBackDecisionAndHistoryAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient moderator) = await ActorAsync(factory, database, "Moderator");
        using (owner)
        using (moderator)
        {
            ImageResponse image = await UploadAsync(owner);
            await ProcessAsync(factory);
            TemplateResponse template = await CreateTemplateAsync(owner, image.Id);
            template = await ActionAsync(owner, template, "submit");
            await SqlAsync(database, "ALTER TABLE notifications.\"EmailDeliveries\" ADD CONSTRAINT reject_workflow CHECK (\"Purpose\" <> 'workflow')", Guid.Empty);
            using HttpResponseMessage response = await moderator.PostAsJsonAsync($"/api/media/templates/{template.Id}/approve", new { template.Version });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            TemplateResponse unchanged = (await owner.GetFromJsonAsync<TemplateResponse>($"/api/media/templates/{template.Id}"))!;
            Assert.Equal(template.Version, unchanged.Version);
            Assert.Equal("Pending", unchanged.Approval);
            Assert.True(unchanged.Submitted);
            Assert.Empty((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            MediaPage<TemplateHistoryResponse> history = (await owner.GetFromJsonAsync<MediaPage<TemplateHistoryResponse>>($"/api/media/templates/{template.Id}/history"))!;
            Assert.DoesNotContain(history.Items, item => item.Action == "approve");
        }
    }

    [Fact]
    public async Task Upload_PrivateFilesAndRevokedAuthor_EnforcesAccessAsync()
    {
        (ApiFactory factory, string database, string endpoint) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid author, HttpClient client) = await ActorAsync(factory, database, "Author");
        using HttpClient owner = client;
        using HttpClient anonymous = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/media/images")).StatusCode);
        ImageResponse image = await UploadAsync(owner);
        Assert.Equal("Processing", image.State);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
        await ProcessAsync(factory);
        image = (await owner.GetFromJsonAsync<ImageResponse>($"/api/media/images/{image.Id}"))!;
        Assert.Equal("Ready", image.State);
        using HttpResponseMessage file = await owner.GetAsync($"/api/media/images/{image.Id}/480");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/webp", file.Content.Headers.ContentType?.MediaType);
        Assert.True(file.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
        foreach (string role in new[] { "Author", "Editor", "Moderator", "Administrator" })
        {
            (_, HttpClient other) = await ActorAsync(factory, database, role);
            using (other)
                Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
        }
        using var direct = new HttpClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await direct.GetAsync(endpoint + "/" + factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaStorageOptions>>().Value.Bucket + $"/images/{image.Id:N}/480.webp")).StatusCode);
        await SqlAsync(database, "DELETE FROM identity.\"AspNetUserRoles\" WHERE \"UserId\" = @id", author);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/media/images")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
    }

    [Fact]
    public async Task Templates_ReviewArchiveBlockAndRemoval_PreserveIndependentStatesAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient moderator) = await ActorAsync(factory, database, "Moderator");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (moderator)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            ImageResponse image = await UploadAsync(owner);
            await ProcessAsync(factory);
            TemplateResponse template = await CreateTemplateAsync(owner, image.Id);
            Assert.False(template.Submitted);
            Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/media/template-review-queue")).StatusCode);
            template = await ActionAsync(owner, template, "submit");
            Assert.Single((await moderator.GetFromJsonAsync<MediaPage<TemplateResponse>>("/api/media/template-review-queue"))!.Items);
            Assert.Equal(HttpStatusCode.BadRequest, (await moderator.PostAsJsonAsync($"/api/media/templates/{template.Id}/reject", new { template.Version })).StatusCode);
            template = await ActionAsync(moderator, template, "reject", "Please remove the private address.");
            Assert.Equal("Rejected", template.Approval);
            template = await ActionAsync(owner, template, "submit");
            template = await ActionAsync(moderator, template, "approve");
            Assert.Single((await owner.GetFromJsonAsync<MediaPage<TemplateResponse>>("/api/media/templates"))!.Items);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            template = await ActionAsync(moderator, template, "block", "Privacy review.");
            template = await ActionAsync(moderator, template, "archive");
            template = await ActionAsync(moderator, template, "restore");
            Assert.True(template.Blocked);
            Assert.False(template.Archived);
            template = await ActionAsync(moderator, template, "unblock", "The block was mistaken.");
            Assert.False(template.Blocked);
            (_, HttpClient reader) = await ActorAsync(factory, database, "Author");
            using (reader)
            {
                TemplateResponse shared = Assert.Single((await reader.GetFromJsonAsync<MediaPage<TemplateResponse>>("/api/media/templates"))!.Items);
                Assert.False(shared.Own);
                Assert.Null(shared.Reason);
                Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/media/templates/{template.Id}/history")).StatusCode);
            }
            using HttpResponseMessage request = await owner.PostAsJsonAsync($"/api/media/templates/{template.Id}/removal-requests", new { });
            RemovalRequestResponse removal = (await request.Content.ReadFromJsonAsync<RemovalRequestResponse>())!;
            RemovalRequestResponse duplicate = (await (await owner.PostAsJsonAsync($"/api/media/templates/{template.Id}/removal-requests", new { })).Content.ReadFromJsonAsync<RemovalRequestResponse>())!;
            Assert.Equal(removal.Id, duplicate.Id);
            Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync($"/api/media/template-removal-requests/{removal.Id}/resolve", new { removal.Version })).StatusCode);
            template = (await owner.GetFromJsonAsync<TemplateResponse>($"/api/media/templates/{template.Id}"))!;
            Assert.True(template.Archived);
            RemovalRequestResponse completedRetry = (await (await owner.PostAsJsonAsync($"/api/media/templates/{template.Id}/removal-requests", new { })).Content.ReadFromJsonAsync<RemovalRequestResponse>())!;
            Assert.Equal(removal.Id, completedRetry.Id);
            Assert.Empty((await moderator.GetFromJsonAsync<MediaPage<RemovalRequestResponse>>("/api/media/template-removal-requests"))!.Items);
            MediaPage<TemplateHistoryResponse> history = (await owner.GetFromJsonAsync<MediaPage<TemplateHistoryResponse>>($"/api/media/templates/{template.Id}/history"))!;
            Assert.Contains(history.Items, entry => entry.Action == "RemovalResolved");
            Assert.Contains(history.Items, entry => entry.Action == "reject" && entry.Reason == "Please remove the private address.");
            AccountNotificationPage notifications = (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!;
            Assert.Equal(new[] { "template.approved", "template.archived", "template.archived", "template.blocked", "template.rejected", "template.unblocked" }, notifications.Items.Select(item => item.Type).Order().ToArray());
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/media/templates/{template.Id}/submit", new { template.Version, imageId = Guid.NewGuid() })).StatusCode);
        }
    }

    [Fact]
    public async Task References_PublicationWithdrawalAndBlock_EnforceCurrentVisibilityAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid author, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid reviewer, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        (_, HttpClient moderator) = await ActorAsync(factory, database, "Administrator");
        using (owner)
        using (editor)
        using (moderator)
        using (HttpClient anonymous = Client(factory))
        {
            ImageResponse image = await UploadAsync(owner);
            await ProcessAsync(factory);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            await ReferencesAsync(factory, database, first, author, reviewer, false, [image.Id]);
            Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            await ReferencesAsync(factory, database, first, author, reviewer, true, [image.Id]);
            await ReferencesAsync(factory, database, second, author, reviewer, true, [image.Id]);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            await ReferencesAsync(factory, database, first, author, null, false, [image.Id]);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            TemplateResponse template = await ActionAsync(owner, await CreateTemplateAsync(owner, image.Id), "submit");
            template = await ActionAsync(moderator, template, "approve");
            template = await ActionAsync(moderator, template, "block", "Unsafe image.");
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            template = await ActionAsync(moderator, template, "unblock", "Confirmed safe.");
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            await ReferencesAsync(factory, database, second, author, null, false, [image.Id]);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
            AccountOperationException limit = await Assert.ThrowsAsync<AccountOperationException>(() => ReferencesAsync(factory, database, Guid.NewGuid(), author, null, false, Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray()));
            Assert.Equal(400, limit.Status);
        }
    }

    [Fact]
    public async Task Cleanup_RetainedReferencesAndUnusedUploads_KeepOnlyProtectedObjectsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid author, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using (owner)
        {
            ImageResponse unused = await UploadAsync(owner);
            ImageResponse retained = await UploadAsync(owner);
            await ProcessAsync(factory);
            await ReferencesAsync(factory, database, Guid.NewGuid(), author, null, false, [retained.Id]);
            await SqlAsync(database, "UPDATE media.\"Images\" SET \"CreatedAtUtc\" = now() - interval '8 days' WHERE \"OwnerId\" = @id", author);
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().CleanupAsync(CancellationToken.None);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/media/images/{unused.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/media/images/{retained.Id}/480")).StatusCode);
            await factory.Services.MigrateMediaAsync(CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/media/images/{retained.Id}/480")).StatusCode);
        }
    }

    [Fact]
    public async Task Processing_StorageOutage_PreservesPayloadAndRetriesAfterRestartAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using (owner)
        {
            ImageResponse image = await UploadAsync(owner);
            await using WebApplicationFactory<CityPortal.Api.Program> unavailable = factory.WithWebHostBuilder(builder => builder.UseSetting("Media:Endpoint", "http://127.0.0.1:1"));
            await using (AsyncServiceScope scope = unavailable.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
            ImageResponse pending = (await owner.GetFromJsonAsync<ImageResponse>($"/api/media/images/{image.Id}"))!;
            Assert.Equal("Processing", pending.State);
            Assert.Equal("storage_unavailable", pending.ErrorCode);
            await SqlAsync(database, "UPDATE media.\"Images\" SET \"NextAttemptAtUtc\" = now() WHERE \"Id\" = @id", image.Id);
            await ProcessAsync(factory);
            Assert.Equal("Ready", (await owner.GetFromJsonAsync<ImageResponse>($"/api/media/images/{image.Id}"))!.State);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
        }
    }

    [Fact]
    public async Task Moderation_StaleVersionAndLostRole_DoNotCommitDecisionsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (Guid moderatorId, HttpClient moderator) = await ActorAsync(factory, database, "Moderator");
        using (owner)
        using (moderator)
        {
            ImageResponse image = await UploadAsync(owner);
            await ProcessAsync(factory);
            TemplateResponse template = await CreateTemplateAsync(owner, image.Id);
            long oldVersion = template.Version;
            template = await ActionAsync(owner, template, "submit");
            Assert.Equal(HttpStatusCode.Conflict, (await moderator.PostAsJsonAsync($"/api/media/templates/{template.Id}/approve", new { version = oldVersion })).StatusCode);
            await SqlAsync(database, "DELETE FROM identity.\"AspNetUserRoles\" WHERE \"UserId\" = @id", moderatorId);
            Assert.Equal(HttpStatusCode.Forbidden, (await moderator.PostAsJsonAsync($"/api/media/templates/{template.Id}/approve", new { template.Version })).StatusCode);
            Assert.Equal("Pending", (await owner.GetFromJsonAsync<TemplateResponse>($"/api/media/templates/{template.Id}"))!.Approval);
        }
    }

    private static async Task<(Guid Id, HttpClient Client)> ActorAsync(ApiFactory factory, string database, string role)
    {
        string email = Guid.NewGuid().ToString("N") + "@media.test";
        AccountResponse account;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            account = await scope.ServiceProvider.GetRequiredService<AccountOperations>().RegisterAsync(email, "MediaAuthor", _password, CancellationToken.None);
        await using (var connection = new NpgsqlConnection(database))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                UPDATE identity."AspNetUsers" SET "EmailConfirmed" = true WHERE "Id" = @id;
                INSERT INTO identity."AspNetRoles" ("Id", "Name", "NormalizedName") VALUES (@roleId, @role, upper(@role)) ON CONFLICT ("NormalizedName") DO NOTHING;
                INSERT INTO identity."AspNetUserRoles" ("UserId", "RoleId") SELECT @id, "Id" FROM identity."AspNetRoles" WHERE "Name" = @role ON CONFLICT DO NOTHING;
                """, connection);
            command.Parameters.AddWithValue("id", account.Id);
            command.Parameters.AddWithValue("roleId", Guid.NewGuid());
            command.Parameters.AddWithValue("role", role);
            await command.ExecuteNonQueryAsync();
        }
        HttpClient client = Client(factory);
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = _password, rememberMe = false })).StatusCode);
        await CsrfAsync(client);
        return (account.Id, client);
    }

    [Fact]
    public async Task Upload_RejectedAttemptsAndMissingCsrf_EnforcePersistentLimitsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using (owner)
        {
            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, await InvalidUploadAsync(owner));
            await CsrfAsync(owner);
            for (int index = 0; index < 19; index++)
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, await InvalidUploadAsync(owner));
            Assert.Equal(HttpStatusCode.TooManyRequests, await InvalidUploadAsync(owner));
            Assert.Empty((await owner.GetFromJsonAsync<MediaPage<ImageResponse>>("/api/media/images"))!.Items);
        }
    }

    [Fact]
    public async Task Cleanup_ConcurrentAttachment_WaitsAndPreservesTheWinningReferenceAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid author, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using (owner)
        {
            ImageResponse image = await UploadAsync(owner);
            await ProcessAsync(factory);
            await SqlAsync(database, "UPDATE media.\"Images\" SET \"CreatedAtUtc\" = now() - interval '8 days' WHERE \"Id\" = @id", image.Id);
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MediaReferences>().ReplaceAsync(connection, transaction, Guid.NewGuid(), author, null, false, false, [image.Id], CancellationToken.None);
            var cleanup = Task.Run(async () =>
            {
                await using AsyncServiceScope cleanupScope = factory.Services.CreateAsyncScope();
                await cleanupScope.ServiceProvider.GetRequiredService<MediaMaintenance>().CleanupAsync(CancellationToken.None);
            });
            bool waiting = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!waiting && !timeout.IsCancellationRequested)
            {
                await using var observer = new NpgsqlConnection(database);
                await observer.OpenAsync();
                await using var query = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%media%Images%')", observer);
                waiting = (bool)(await query.ExecuteScalarAsync())!;
                if (!waiting)
                    await Task.Delay(25);
            }
            Assert.True(waiting, "Cleanup must wait on the same image lock used by attachment.");
            await transaction.CommitAsync();
            await cleanup;
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/media/images/{image.Id}/480")).StatusCode);
        }
    }

    private static async Task<HttpStatusCode> InvalidUploadAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("not an image"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "invalid.png");
        using HttpResponseMessage response = await client.PostAsync("/api/media/images", form);
        return response.StatusCode;
    }

    [Fact]
    public async Task Restore_MetadataAndProcessedObjects_PreservesPrivateReadsAndPendingWorkAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (Guid author, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using (owner)
        {
            ImageResponse published = await UploadAsync(owner);
            ImageResponse privateImage = await UploadAsync(owner);
            await ProcessAsync(factory);
            ImageResponse pending = await UploadAsync(owner);
            await ReferencesAsync(factory, database, Guid.NewGuid(), author, null, true, [published.Id]);
            byte[] expected = await owner.GetByteArrayAsync($"/api/media/images/{published.Id}/480");
            await using ApiFactory restored = await _fixture.RestoreAsync(factory, database);
            using HttpClient anonymous = Client(restored);
            Assert.Equal(expected, await anonymous.GetByteArrayAsync($"/api/media/images/{published.Id}/480"));
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{privateImage.Id}/480")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{pending.Id}/480")).StatusCode);
            await ProcessAsync(restored);
            string email = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!.Email;
            await CsrfAsync(anonymous);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = _password, rememberMe = false })).StatusCode);
            Assert.Equal("Ready", (await anonymous.GetFromJsonAsync<ImageResponse>($"/api/media/images/{pending.Id}"))!.State);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/media/images/{privateImage.Id}/480")).StatusCode);
        }
    }

    private static HttpClient Client(ApiFactory factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task CsrfAsync(HttpClient client)
    {
        JsonElement csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task<ImageResponse> UploadAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(MediaProcessorTests.Image());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "park.png");
        using HttpResponseMessage response = await client.PostAsync("/api/media/images", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImageResponse>())!;
    }

    private static async Task<TemplateResponse> CreateTemplateAsync(HttpClient client, Guid imageId)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/media/templates", new { imageId, name = "River path" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateResponse>())!;
    }

    private static async Task<TemplateResponse> ActionAsync(HttpClient client, TemplateResponse template, string action, string? reason = null)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/media/templates/{template.Id}/{action}", new { template.Version, reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateResponse>())!;
    }

    private static async Task ProcessAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
    }

    private static async Task ReferencesAsync(ApiFactory factory, string database, Guid revision, Guid author, Guid? reviewer, bool isPublic, Guid[] ids)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediaReferences>().ReplaceAsync(connection, transaction, revision, author, reviewer, true, isPublic, ids, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static async Task SqlAsync(string database, string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
}
