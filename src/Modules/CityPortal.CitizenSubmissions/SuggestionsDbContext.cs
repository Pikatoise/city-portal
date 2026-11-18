using Microsoft.EntityFrameworkCore;

namespace CityPortal.CitizenSubmissions;

internal sealed class SuggestionsDbContext : DbContext
{
    public SuggestionsDbContext(DbContextOptions<SuggestionsDbContext> options) : base(options)
    {
    }

    public DbSet<Suggestion> Suggestions => Set<Suggestion>();

    public DbSet<SuggestionDeletion> Deletions => Set<SuggestionDeletion>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("citizen_submissions");
        model.Entity<Suggestion>(item =>
        {
            item.HasIndex(value => value.ExpiresAtUtc);
            item.HasIndex(value => new { value.ResidentId, value.CreatedAtUtc, value.Id });
            item.HasIndex(value => new { value.AssigneeId, value.ExpiresAtUtc, value.Id });
            item.Property(value => value.Title).HasMaxLength(200);
            item.Property(value => value.Description).HasMaxLength(10000);
            item.Property(value => value.Location).HasMaxLength(300);
            item.Property(value => value.Contact).HasMaxLength(300);
        });
        model.Entity<SuggestionDeletion>(item =>
        {
            item.HasKey(value => value.SuggestionId);
            item.Property(value => value.Reason).HasMaxLength(1000);
        });
    }
}
