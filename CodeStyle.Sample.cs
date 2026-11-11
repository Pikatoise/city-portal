// Temporary style review sample. Edit this file to show your preferred style.
// This is illustrative code, not an implementation of the account workflow.
// Multiple types are kept together here only to make the review easier.

#nullable enable

// Using directives are outside the namespace, with System namespaces first.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// File-scoped namespace.
namespace CityPortal.StyleSample;

// PascalCase for types, methods, and properties; interfaces start with I.
public interface IAccountRepository
{
    Task<Account?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken);
}

public sealed class AccountReader
{
    // Private fields use _camelCase; constructor dependencies are readonly.
    private readonly IAccountRepository _accounts;

    // Ordinary constructor, rather than a primary constructor.
    public AccountReader(IAccountRepository accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        _accounts = accounts;
    }

    // Async suffix; propagate CancellationToken to the I/O operation.
    // Long parameter lists can be split onto separate lines.
    public async Task<AccountSummary?> GetSummaryAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Four spaces; opening braces on a new line, including single statements.
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        // Explicit type when the initializer does not make the type obvious.
        // Nullable references are checked instead of suppressed with !.
        Account? account = await _accounts.FindByIdAsync(accountId, cancellationToken);

        if (account is null)
            return null;

        // var when the type is obvious from the initializer.
        var summary = new AccountSummary
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            EmailConfirmed = account.EmailConfirmed,
            Roles = account.Roles,
        };

        return summary;
    }

    public static bool HasRole(AccountSummary account, string roleName)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        // Braces for loops too; camelCase for parameters and local variables.
        foreach (string role in account.Roles)
            // Role identifiers are stable values, so comparison ignores culture.
            if (string.Equals(role, roleName, StringComparison.Ordinal))
                return true;

        return false;
    }
}

public sealed class Account
{
    public Guid Id { get; init; }

    // Use string, bool, and int instead of String, Boolean, and Int32.
    public string DisplayName { get; init; } = string.Empty;

    public bool EmailConfirmed { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

public sealed class AccountSummary
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool EmailConfirmed { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    // Expression bodies for short, single-line expressions.
    public int RoleCount => Roles.Count;

    public override string ToString() => $"{DisplayName} ({Id})";
}
