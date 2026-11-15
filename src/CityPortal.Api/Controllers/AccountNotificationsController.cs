using System.ComponentModel.DataAnnotations;
using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Authorize]
[Route("api/account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountNotificationsController : ControllerBase
{
    private readonly AccountOperations _accounts;
    private readonly AccountNotifications _notifications;

    public AccountNotificationsController(AccountOperations accounts, AccountNotifications notifications)
    {
        _accounts = accounts;
        _notifications = notifications;
    }

    private async Task<Guid> AccountIdAsync(CancellationToken cancellationToken)
    {
        AccountResponse? account = await _accounts.FindSessionAsync(Guid.Parse(User.FindFirst("session")!.Value), cancellationToken);
        return account?.Id ?? throw new AccountOperationException(401, "invalid_session", "Log in again.");
    }

    [HttpGet("notifications", Name = "listAccountNotifications")]
    [ProducesResponseType<AccountNotificationPage>(200)]
    public async Task<AccountNotificationPage> ListAsync(CancellationToken cancellationToken, [Range(1, 100000)] int page = 1, [Range(1, 100)] int pageSize = 20) =>
        await _notifications.ListAsync(await AccountIdAsync(cancellationToken), page, pageSize, cancellationToken);

    [HttpPost("notifications/{id:guid}/read", Name = "readAccountNotification")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ReadAsync(Guid id, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        await _notifications.MarkReadAsync(await AccountIdAsync(cancellationToken), id, cancellationToken) ? NoContent() : NotFound();

    [HttpDelete("notifications/{id:guid}", Name = "deleteAccountNotification")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteAsync(Guid id, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken) =>
        await _notifications.DeleteAsync(await AccountIdAsync(cancellationToken), id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("notification-preferences", Name = "getNotificationPreferences")]
    [ProducesResponseType<NotificationPreferenceResponse[]>(200)]
    public async Task<NotificationPreferenceResponse[]> PreferencesAsync(CancellationToken cancellationToken) =>
        await _notifications.PreferencesAsync(await AccountIdAsync(cancellationToken), cancellationToken);

    [HttpPut("notification-preferences", Name = "saveNotificationPreferences")]
    [RequestSizeLimit(16384)]
    [Consumes("application/json")]
    [ProducesResponseType<NotificationPreferenceResponse[]>(200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<NotificationPreferenceResponse[]>> SaveAsync(NotificationPreferenceResponse[] request, [FromHeader(Name = "X-CSRF-TOKEN"), Required] string csrfToken, CancellationToken cancellationToken)
    {
        Guid accountId = await AccountIdAsync(cancellationToken);
        if (request.Length > WorkflowNotificationTypes.All.Count || request.Any(item => item is null || item.Type is null || !WorkflowNotificationTypes.Contains(item.Type)) || request.Select(item => item.Type).Distinct().Count() != request.Length)
            throw new AccountOperationException(400, "invalid_preferences", "Provide each supported workflow type at most once. Authentication mail cannot be disabled.");
        await _notifications.SavePreferencesAsync(accountId, request, cancellationToken);
        return await _notifications.PreferencesAsync(accountId, cancellationToken);
    }
}
