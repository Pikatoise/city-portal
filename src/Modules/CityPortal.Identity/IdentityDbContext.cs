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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<Account>().Property(account => account.DisplayName).HasMaxLength(20);
        builder.Entity<Account>().Property(account => account.Email).HasMaxLength(254);
        builder.Entity<Account>().Property(account => account.NormalizedEmail).HasMaxLength(254);
        builder.Entity<Account>().HasIndex(account => account.NormalizedEmail).IsUnique();
        builder.Entity<Account>().Property(account => account.Version).IsConcurrencyToken();
        builder.Entity<AccountLink>().HasIndex(link => new { link.AccountId, link.CreatedAtUtc });
        builder.Entity<AccountLink>().Property(link => link.TokenHash).HasMaxLength(64);
        builder.Entity<AccountLink>().Property(link => link.State).HasMaxLength(20);
        builder.Entity<AccountLink>().HasOne<Account>().WithMany().HasForeignKey(link => link.AccountId);
        builder.Entity<AccountSession>().HasOne<Account>().WithMany().HasForeignKey(session => session.AccountId);
        builder.Entity<RequestAttempt>().Property(attempt => attempt.Key).HasMaxLength(100);
        builder.Entity<RequestAttempt>().HasIndex(attempt => new { attempt.Key, attempt.CreatedAtUtc });
    }
}
