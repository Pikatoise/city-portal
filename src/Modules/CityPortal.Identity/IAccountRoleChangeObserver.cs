using System.Data.Common;

namespace CityPortal.Identity;

/// <summary>Joins role changes before commit to maintain dependent module permissions atomically.</summary>
public interface IAccountRoleChangeObserver
{
    Task RolesChangedAsync(Guid actorId, Guid accountId, string[] oldRoles, string[] newRoles, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken);
    Task FlushAfterCommitAsync();
}
