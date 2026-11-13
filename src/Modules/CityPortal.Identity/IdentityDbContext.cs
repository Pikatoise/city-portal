using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Identity;

internal sealed class IdentityDbContext : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<Account, IdentityRole<Guid>, Guid>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    public DbSet<AccountLink> Links => Set<AccountLink>();
    public DbSet<AccountSession> Sessions => Set<AccountSession>();
    public DbSet<RequestAttempt> RequestAttempts => Set<RequestAttempt>();
    public DbSet<RoleChange> RoleChanges => Set<RoleChange>();
    public DbSet<SiteConfiguration> Sites => Set<SiteConfiguration>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<SiteConfiguration>().ToTable("SiteConfiguration");
        builder.Entity<Account>().Property(account => account.DisplayName).HasMaxLength(20);
        builder.Entity<Account>().Property(account => account.Email).HasMaxLength(254);
        builder.Entity<Account>().Property(account => account.NormalizedEmail).HasMaxLength(254);
        builder.Entity<Account>().HasIndex(account => account.NormalizedEmail).IsUnique();
        builder.Entity<Account>().Property(account => account.Version).IsConcurrencyToken();
        builder.Entity<AccountLink>().HasIndex(link => new { link.AccountId, link.CreatedAtUtc });
        builder.Entity<AccountLink>().Property(link => link.TokenHash).HasMaxLength(64);
        builder.Entity<AccountLink>().Property(link => link.State).HasMaxLength(20);
        builder.Entity<AccountLink>().Property(link => link.Purpose).HasMaxLength(20).HasDefaultValue("confirmation");
        builder.Entity<AccountLink>().HasOne<Account>().WithMany().HasForeignKey(link => link.AccountId);
        builder.Entity<AccountSession>().HasOne<Account>().WithMany().HasForeignKey(session => session.AccountId);
        builder.Entity<RequestAttempt>().Property(attempt => attempt.Key).HasMaxLength(100);
        builder.Entity<RequestAttempt>().HasIndex(attempt => new { attempt.Key, attempt.CreatedAtUtc });
        builder.Entity<RoleChange>().Property(change => change.ActorKind).HasMaxLength(20);
        builder.Entity<RoleChange>().HasIndex(change => new { change.TargetAccountId, change.CreatedAtUtc });
    }
}
