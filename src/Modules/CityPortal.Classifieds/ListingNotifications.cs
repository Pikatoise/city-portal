using CityPortal.Identity;
using CityPortal.Notifications;

namespace CityPortal.Classifieds;

internal sealed class ListingNotifications
{
    private readonly AccountOperations _accounts;
    private readonly WorkflowNotificationQueue _queue;

    public ListingNotifications(AccountOperations accounts, WorkflowNotificationQueue queue)
    {
        _accounts = accounts;
        _queue = queue;
    }

    public async Task QueueAsync(AuthorizedAccountScope scope, ListingDraft draft, string type, DateTimeOffset now, CancellationToken cancellationToken)
    {
        WorkflowRecipient[] recipients = await _accounts.FindNotificationRecipientsAsync([draft.OwnerId], cancellationToken);
        await _queue.QueueAsync(Guid.NewGuid(), type, draft.Id, now, recipients, scope.Connection, scope.Transaction, cancellationToken);
    }

    public Task FlushAfterCommitAsync() => _queue.FlushAfterCommitAsync();
}
