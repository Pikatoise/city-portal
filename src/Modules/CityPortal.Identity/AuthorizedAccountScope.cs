using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Identity;

public sealed class AuthorizedAccountScope : IAsyncDisposable
{
    private readonly IDbContextTransaction _transaction;

    internal AuthorizedAccountScope(AccountResponse actor, DbConnection connection, IDbContextTransaction transaction)
    {
        Actor = actor;
        Connection = connection;
        _transaction = transaction;
    }

    public AccountResponse Actor { get; }

    public DbConnection Connection { get; }

    public DbTransaction Transaction => _transaction.GetDbTransaction();

    public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
