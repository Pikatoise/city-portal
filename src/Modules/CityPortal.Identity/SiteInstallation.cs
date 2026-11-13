using CityPortal.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Identity;

public sealed class SiteInstallation
{
    private readonly IdentityDbContext _context;
    private readonly MailConfigurationStore _mail;
    private readonly TimeProvider _time;

    internal SiteInstallation(IdentityDbContext context, MailConfigurationStore mail, TimeProvider time)
    {
        _context = context;
        _mail = mail;
        _time = time;
    }

    public async Task<InstallationInfo> GetAsync(CancellationToken cancellationToken)
    {
        SiteConfiguration? site = await _context.Sites.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return new InstallationInfo(site is not null, site?.Name, site?.City, site?.Region, site?.TimeZone);
    }

    public async Task CompleteAsync(string email, string displayName, string password, string name, string city, string region, string timeZone,
        AccountMailOptions mail, CancellationToken cancellationToken)
    {
        (string cleanEmail, string cleanName) = AccountValidation.ValidateRegistration(email, displayName, password);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || string.IsNullOrWhiteSpace(city) || city.Trim().Length > 120 || region.Trim().Length > 120)
            throw new AccountOperationException(400, "invalid_site", "Provide a site name and city of at most 120 characters; region is optional.");
        try
        {
            if (timeZone != "UTC" && TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone, out string? converted))
                timeZone = converted;
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZone).Id;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new AccountOperationException(400, "invalid_time_zone", "Select a valid time zone.");
        }
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // Share the bootstrap/role lock so the first administrator and installation can be created only once.
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('administration:roles', 0))", cancellationToken);
        if (await _context.Sites.AnyAsync(cancellationToken) || await _context.Users.AnyAsync(cancellationToken))
            throw new AccountOperationException(409, "already_installed", "The site is already installed.");
        mail.PortalName = name.Trim();
        await _mail.TestAsync(mail, cleanEmail, cancellationToken);
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = cleanEmail,
            UserName = cleanEmail,
            NormalizedEmail = cleanEmail.ToUpperInvariant(),
            NormalizedUserName = cleanEmail.ToUpperInvariant(),
            DisplayName = cleanName,
            EmailConfirmed = true,
            CreatedAtUtc = _time.GetUtcNow(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
        account.PasswordHash = new PasswordHasher<Account>().HashPassword(account, password);
        _context.Users.Add(account);
        foreach (string roleName in new[] { "RegularUser", "Administrator" })
        {
            IdentityRole<Guid>? role = await _context.Roles.SingleOrDefaultAsync(item => item.Name == roleName, cancellationToken);
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid(), NormalizedName = roleName.ToUpperInvariant() };
                _context.Roles.Add(role);
            }
            _context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = account.Id, RoleId = role.Id });
        }
        _context.RoleChanges.Add(new RoleChange
        {
            Id = Guid.NewGuid(),
            ActorKind = "Bootstrap",
            TargetAccountId = account.Id,
            CreatedAtUtc = _time.GetUtcNow(),
            OldRoles = [],
            NewRoles = ["Administrator", "RegularUser"]
        });
        _context.Sites.Add(new SiteConfiguration { Name = name.Trim(), City = city.Trim(), Region = region.Trim(), TimeZone = timeZone });
        await _context.SaveChangesAsync(cancellationToken);
        await _mail.SaveAsync(mail, _context.Database.GetDbConnection(), transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
