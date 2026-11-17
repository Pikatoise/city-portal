namespace CityPortal.Notifications;

/// <summary>A bounded page from the current account's notification list.</summary>
public sealed record AccountNotificationPage(AccountNotificationResponse[] Items, int Total, int Page, int PageSize);
