using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CityPortal.Identity;

internal sealed class IdentityDbContext : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<Account, IdentityRole<Guid>, Guid>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<Account>().Property(account => account.DisplayName).HasMaxLength(20);
        builder.Entity<Account>().Property(account => account.Email).HasMaxLength(254);
        builder.Entity<Account>().Property(account => account.NormalizedEmail).HasMaxLength(254);
        builder.Entity<Account>().HasIndex(account => account.NormalizedEmail).IsUnique();
    }
}
