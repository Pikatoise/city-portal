using Microsoft.EntityFrameworkCore;

namespace CityPortal.CitizenSubmissions;

internal sealed class SuggestionsDbContext : DbContext
{
    public SuggestionsDbContext(DbContextOptions<SuggestionsDbContext> options) : base(options)
    {
    }

    public DbSet<Suggestion> Suggestions => Set<Suggestion>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("citizen_submissions");
        model.Entity<Suggestion>(item =>
        {
            item.HasIndex(value => value.ExpiresAtUtc);
            item.HasIndex(value => new { value.ResidentId, value.CreatedAtUtc, value.Id });
            item.Property(value => value.Title).HasMaxLength(200);
            item.Property(value => value.Description).HasMaxLength(10000);
            item.Property(value => value.Location).HasMaxLength(300);
            item.Property(value => value.Contact).HasMaxLength(300);
        });
    }
}
