namespace CityPortal.Identity;

/// <summary>Applies a block to module data in the Identity transaction.</summary>
public interface IAccountBlockObserver
{
    Task HideForAccountBlockAsync(Guid accountId, AuthorizedAccountScope scope, CancellationToken cancellationToken);
}
