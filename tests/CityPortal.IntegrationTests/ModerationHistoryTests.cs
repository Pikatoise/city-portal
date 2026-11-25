using System.Net;
using System.Net.Http.Json;
using CityPortal.Classifieds;
using CityPortal.Identity;
using CityPortal.Notifications;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class ModerationHistoryTests : IClassFixture<RegistrationFixture>
{
    private readonly RegistrationFixture _fixture;

    public ModerationHistoryTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListingDecisions_UseIndependentChannelsAndKeepStaffHistoryAfterDeletionAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: true);
        using HttpClient other = await CitizenSuggestionTests.LoginAsync(factory, database, "Other", confirmed: true);
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "Moderator", confirmed: true, role: "Moderator");
        AccountResponse ownerAccount = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        AccountResponse moderatorAccount = (await moderator.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/account/notification-preferences", new[]
        {
            new NotificationPreferenceResponse("listing.returned", true, false),
            new NotificationPreferenceResponse("listing.rejected", false, true)
        })).StatusCode);

        ListingDraftResponse listing = await ClassifiedsPublicationTests.SubmitAsync(owner, admin, "Central");
        string staffPath = $"/api/staff/classifieds/review/{listing.Id}";
        string ownerPath = $"/api/account/classifieds/drafts/{listing.Id}";
        const string returnReason = "Add the dimensions.";
        const string rejectReason = "This listing does not follow the rules.";
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync(staffPath + "/return",
            new DecideListingReviewRequest { Version = listing.Version, Reason = returnReason })).StatusCode);
        ListingDraftResponse returned = (await owner.GetFromJsonAsync<ListingDraftResponse>(ownerPath))!;
        ListingDraftResponse resubmitted = await CitizenSuggestionTests.ReadAsync<ListingDraftResponse>(
            await owner.PostAsJsonAsync(ownerPath + "/submit", new ListingDraftVersionRequest { Version = returned.Version }));
        clock.Advance(TimeSpan.FromMinutes(1));
        var decision = new DecideListingReviewRequest { Version = resubmitted.Version, Reason = rejectReason };
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync(staffPath + "/reject", decision)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await moderator.PostAsJsonAsync(staffPath + "/reject", decision)).StatusCode);

        AccountNotificationResponse notice = Assert.Single((await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
        Assert.Equal(("listing.returned", listing.Id), (notice.Type, notice.ResourceId));
        Assert.DoesNotContain(returnReason, notice.Message);
        Assert.Equal(new[] { "listing.rejected" }, await WorkflowMailTypesAsync(database, ownerAccount.Id));
        Assert.Empty((await moderator.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!.Items);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/account/notifications/{notice.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(ownerPath + "/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/staff/classifieds/history")).StatusCode);

        ClassifiedsPage<ListingHistoryItem> history = (await owner.GetFromJsonAsync<ClassifiedsPage<ListingHistoryItem>>(ownerPath + "/history"))!;
        Assert.False(history.HasNextPage);
        Assert.Collection(history.Items,
            item => Assert.Equal(("Rejected", rejectReason, resubmitted.Version), (item.Outcome, item.Reason, item.Version)),
            item => Assert.Equal(("Returned", returnReason, listing.Version), (item.Outcome, item.Reason, item.Version)));
        Assert.True(history.Items[0].DecidedAtUtc > history.Items[1].DecidedAtUtc);
        Assert.All(history.Items, item => { Assert.Null(item.ActorId); Assert.Null(item.ActorName); });

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{ownerPath}?version={resubmitted.Version + 1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(ownerPath + "/history")).StatusCode);
        ClassifiedsPage<ListingHistoryItem> archive = (await admin.GetFromJsonAsync<ClassifiedsPage<ListingHistoryItem>>("/api/staff/classifieds/history"))!;
        Assert.Equal(history.Items.Select(item => item.Outcome), archive.Items.Select(item => item.Outcome));
        Assert.All(archive.Items, item =>
        {
            Assert.Equal(listing.Id, item.ListingId);
            Assert.Equal(moderatorAccount.Id, item.ActorId);
            Assert.Equal(moderatorAccount.DisplayName, item.ActorName);
            Assert.Null(item.CurrentTitle);
        });
    }

    [Fact]
    public async Task AccountBlockHistory_UsesCurrentStaffPermissionsAndKeepsUnverifiedEmailPrivateAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient owner = await CitizenSuggestionTests.LoginAsync(factory, database, "Owner", confirmed: false);
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "Moderator", confirmed: true, role: "Moderator");
        using HttpClient admin = await CitizenSuggestionTests.LoginAsync(factory, database, "Admin", confirmed: true, role: "Administrator");
        AccountResponse account = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        AccountResponse staff = (await moderator.GetFromJsonAsync<AccountResponse>("/api/account"))!;
        string target = $"/api/staff/account-blocks/{account.Id}";
        string staffTarget = $"/api/staff/account-blocks/{staff.Id}";
        const string reason = "Repeated abuse.";
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync(target + "/block", new { account.Version, Reason = reason })).StatusCode);
        AccountBlockHistory own = (await owner.GetFromJsonAsync<AccountBlockHistory>("/api/account/block-history"))!;
        AccountBlockHistoryItem first = Assert.Single(own.Items);
        Assert.True(first.Blocked);
        Assert.Equal(reason, first.Reason);
        Assert.Null(first.ActorId);
        Assert.Null(first.ActorName);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(target + "/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.GetAsync(staffTarget + "/history")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<AccountBlockHistory>(staffTarget + "/history"))!.Items);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync(target + "/unblock", new { Version = account.Version + 1 })).StatusCode);
        AccountBlockHistory history = (await moderator.GetFromJsonAsync<AccountBlockHistory>(target + "/history"))!;
        Assert.Collection(history.Items,
            item => { Assert.False(item.Blocked); Assert.Null(item.Reason); },
            item => { Assert.True(item.Blocked); Assert.Equal(reason, item.Reason); });
        Assert.All(history.Items, item =>
        {
            Assert.Equal(staff.Id, item.ActorId);
            Assert.Equal(staff.DisplayName, item.ActorName);
        });
        AccountNotificationPage notices = (await owner.GetFromJsonAsync<AccountNotificationPage>("/api/account/notifications"))!;
        Assert.Equal(new[] { "account.unblocked", "account.blocked" }, notices.Items.Select(item => item.Type));
        Assert.All(notices.Items, item =>
        {
            Assert.Equal(account.Id, item.ResourceId);
            Assert.DoesNotContain(reason, item.Message);
        });
        Assert.Empty(await WorkflowMailTypesAsync(database, account.Id));
    }

    private static async Task<string[]> WorkflowMailTypesAsync(string database, Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT "NotificationType" FROM notifications."EmailDeliveries" WHERE "AccountId" = @id AND "Purpose" = 'workflow'
            ORDER BY "NotificationType"
            """, connection);
        command.Parameters.AddWithValue("id", accountId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        var types = new List<string>();
        while (await reader.ReadAsync())
            types.Add(reader.GetString(0));
        return types.ToArray();
    }
}
