using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Identity;

internal sealed class AccountMailLinkValidator : IAccountMailLinkValidator
{
    private readonly IdentityDbContext _context;
    private readonly TimeProvider _time;

    public AccountMailLinkValidator(IdentityDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public Task<bool> IsActiveAsync(Guid accountId, Guid linkId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        return _context.Links.AnyAsync(link => link.Id == linkId && link.AccountId == accountId && link.State == "Active" && link.ExpiresAtUtc > now, cancellationToken);
    }
}
