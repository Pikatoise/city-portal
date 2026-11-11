namespace CityPortal.Notifications;

public interface IAccountMailLinkValidator
{
    Task<bool> IsActiveAsync(Guid accountId, Guid linkId, CancellationToken cancellationToken);
}
