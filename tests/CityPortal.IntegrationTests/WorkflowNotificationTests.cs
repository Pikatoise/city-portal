using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class WorkflowNotificationTests : IClassFixture<RegistrationFixture>
{
    private readonly RegistrationFixture _fixture;

    public WorkflowNotificationTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Notifications_OwnershipCsrfAndPreferences_KeepChannelsIndependentAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (WorkflowRecipient recipient, HttpClient owner) = await ActorAsync(factory);
        (_, HttpClient other) = await ActorAsync(factory);
        using (owner)
        using (other)
        using (HttpClient anonymous = Client(factory))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/account/notifications")).StatusCode);
            NotificationPreferenceResponse[] defaults = (await owner.GetFromJsonAsync<NotificationPreferenceResponse[]>("/api/account/notification-preferences"))!;
            Assert.Equal(18, defaults.Length);
            Assert.All(defaults, item => { Assert.True(item.Account); Assert.True(item.Email); });
            Assert.DoesNotContain(defaults, item => item.Type is "confirmation" or "recovery");
            var preference = new NotificationPreferenceResponse("template.approved", true, false);
            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/account/notification-preferences", new[] { preference })).StatusCode);
            await CsrfAsync(owner);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/account/notification-preferences", new[] { new NotificationPreferenceResponse("confirmation", false, false) })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/account/notification-preferences", new[] { preference, preference })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/account/notification-preferences", new[] { preference })).StatusCode);
            var eventId = Guid.NewGuid();
            await QueueAsync(factory, database, eventId, "template.approved", recipient, DateTimeOffset.UtcNow);
            await QueueAsync(factory, database, eventId, "template.approved", recipient, DateTimeOffset.UtcNow);
            AccountNotificationResponse notification = Assert.Single((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.False(notification.Read);
            Assert.Equal(0L, await WorkflowMailCountAsync(database, recipient.AccountId));
            Assert.Empty((await other.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/account/notifications/{notification.Id}/read", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/account/notifications/{notification.Id}")).StatusCode);
            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/account/notifications/{notification.Id}/read", new { })).StatusCode);
            await CsrfAsync(owner);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/account/notifications/{notification.Id}/read", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/account/notifications/{notification.Id}/read", new { })).StatusCode);
            Assert.True(Assert.Single((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items).Read);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/account/notifications/{notification.Id}")).StatusCode);
            await QueueAsync(factory, database, eventId, "template.approved", recipient, DateTimeOffset.UtcNow);
            Assert.Empty((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/account/notification-preferences", new[] { preference with { Account = false, Email = true } })).StatusCode);
            await QueueAsync(factory, database, Guid.NewGuid(), "template.approved", recipient, DateTimeOffset.UtcNow);
            Assert.Empty((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.Equal(1L, await WorkflowMailCountAsync(database, recipient.AccountId));
            Assert.All((await other.GetFromJsonAsync<NotificationPreferenceResponse[]>("/api/account/notification-preferences"))!, item => Assert.True(item.Account && item.Email));
        }
    }

    [Fact]
    public async Task Notifications_RollbackDuplicateAndRetention_PreserveDurabilityBoundariesAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        (WorkflowRecipient recipient, HttpClient owner) = await ActorAsync(factory);
        using (owner)
        {
            DateTimeOffset created = time.GetUtcNow();
            var eventId = Guid.NewGuid();
            await QueueAsync(factory, database, eventId, "template.rejected", recipient, created, commit: false);
            Assert.Empty((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            Assert.Equal(0L, await WorkflowMailCountAsync(database, recipient.AccountId));
            await QueueAsync(factory, database, eventId, "template.rejected", recipient, created);
            await QueueAsync(factory, database, eventId, "template.rejected", recipient, created);
            Assert.Equal(1L, await WorkflowMailCountAsync(database, recipient.AccountId));
            AccountNotificationResponse notice = Assert.Single((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            await owner.PostAsJsonAsync($"/api/account/notifications/{notice.Id}/read", new { });
            time.Advance(TimeSpan.FromDays(30).Subtract(TimeSpan.FromTicks(10)));
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
                Assert.Single((await scope.ServiceProvider.GetRequiredService<AccountNotifications>().ListAsync(recipient.AccountId, 1, 20, CancellationToken.None)).Items);
            time.Advance(TimeSpan.FromTicks(10));
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            {
                AccountNotifications notifications = scope.ServiceProvider.GetRequiredService<AccountNotifications>();
                Assert.Empty((await notifications.ListAsync(recipient.AccountId, 1, 20, CancellationToken.None)).Items);
            }
            await QueueAsync(factory, database, eventId, "template.rejected", recipient, created);
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
                Assert.Empty((await scope.ServiceProvider.GetRequiredService<AccountNotifications>().ListAsync(recipient.AccountId, 1, 20, CancellationToken.None)).Items);
            Assert.Equal(1L, await WorkflowMailCountAsync(database, recipient.AccountId));
        }
    }

    [Fact]
    public async Task Notifications_PaginationAndExpiredHandoff_UseEventTimeAndStableOrderingAsync()
    {
        var time = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(time);
        await using ApiFactory lifetime = factory;
        (WorkflowRecipient recipient, HttpClient owner) = await ActorAsync(factory);
        using (owner)
        {
            DateTimeOffset now = time.GetUtcNow();
            await QueueAsync(factory, database, Guid.NewGuid(), "template.approved", recipient, now.AddDays(-7));
            Assert.Equal(0L, await WorkflowMailCountAsync(database, recipient.AccountId));
            await QueueAsync(factory, database, Guid.NewGuid(), "template.approved", recipient, now.AddDays(-30));
            Assert.Single((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
            for (int index = 0; index < 21; index++)
                await QueueAsync(factory, database, Guid.NewGuid(), "template.approved", recipient, now.AddMinutes(-index));
            AccountNotificationPage first = (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications?page=1&pageSize=20"))!;
            AccountNotificationPage second = (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications?page=2&pageSize=20"))!;
            Assert.Equal(22, first.Total);
            Assert.Equal(20, first.Items.Length);
            Assert.Equal(2, second.Items.Length);
            Assert.Equal(first.Items.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id).Select(item => item.Id), first.Items.Select(item => item.Id));
            Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/account/notifications?page=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/account/notifications?page=2147483647&pageSize=100")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/account/notifications?pageSize=101")).StatusCode);
        }
    }

    private static async Task QueueAsync(ApiFactory factory, string database, Guid eventId, string type, WorkflowRecipient recipient, DateTimeOffset created, bool commit = true)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        WorkflowNotificationQueue queue = scope.ServiceProvider.GetRequiredService<WorkflowNotificationQueue>();
        await queue.QueueAsync(eventId, type, eventId, created, [recipient, recipient], connection, transaction, CancellationToken.None);
        if (commit)
            await transaction.CommitAsync();
        else
            await transaction.RollbackAsync();
    }

    private static async Task<long> WorkflowMailCountAsync(string database, Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"AccountId\" = @id AND \"Purpose\" = 'workflow'", connection);
        command.Parameters.AddWithValue("id", accountId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static HttpClient Client(ApiFactory factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task CsrfAsync(HttpClient client)
    {
        JsonElement csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task<(WorkflowRecipient Recipient, HttpClient Client)> ActorAsync(ApiFactory factory)
    {
        string email = Guid.NewGuid().ToString("N") + "@workflow.test";
        using HttpClient client = Client(factory);
        await CsrfAsync(client);
        using HttpResponseMessage registered = await client.PostAsJsonAsync("/api/auth/register", new { email, displayName = "WorkflowUser", password = "WorkflowPassword1!" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        AccountResponse account = (await registered.Content.ReadFromJsonAsync<AccountResponse>())!;
        HttpClient session = Client(factory);
        await CsrfAsync(session);
        Assert.Equal(HttpStatusCode.OK, (await session.PostAsJsonAsync("/api/auth/login", new { email, password = "WorkflowPassword1!", rememberMe = false })).StatusCode);
        await CsrfAsync(session);
        return (new WorkflowRecipient(account.Id, email), session);
    }
}
