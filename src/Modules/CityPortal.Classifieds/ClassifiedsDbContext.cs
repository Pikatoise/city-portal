using Microsoft.EntityFrameworkCore;

namespace CityPortal.Classifieds;

internal sealed class ClassifiedsDbContext : DbContext
{
    public ClassifiedsDbContext(DbContextOptions<ClassifiedsDbContext> options) : base(options)
    {
    }

    internal DbSet<CatalogEntry> Catalog => Set<CatalogEntry>();

    internal DbSet<ListingDraft> Drafts => Set<ListingDraft>();

    internal DbSet<ListingReviewDecision> ReviewDecisions => Set<ListingReviewDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("classifieds");
        modelBuilder.Entity<CatalogEntry>(entry =>
        {
            entry.Property(item => item.Kind).HasMaxLength(10);
            entry.Property(item => item.Name).HasMaxLength(120);
            entry.HasIndex(item => new { item.Kind, item.NormalizedName }).IsUnique();
            entry.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<ListingReviewDecision>(decision =>
        {
            decision.HasKey(item => new { item.ListingId, item.SubmittedVersion });
            decision.Property(item => item.Outcome).HasMaxLength(8);
            decision.Property(item => item.Reason).HasMaxLength(500);
        });
        modelBuilder.Entity<ListingDraft>(draft =>
        {
            draft.HasIndex(item => new { item.OwnerId, item.UpdatedAtUtc, item.Id }).IsDescending(false, true, true);
            draft.HasIndex(item => new { item.SubmittedAtUtc, item.Id }).HasFilter("\"SubmittedAtUtc\" IS NOT NULL");
            draft.Property(item => item.ReviewOutcome).HasMaxLength(8);
            draft.Property(item => item.Title).HasMaxLength(200);
            draft.Property(item => item.Description).HasMaxLength(10000);
            draft.Property(item => item.Phone).HasMaxLength(50);
            draft.Property(item => item.Email).HasMaxLength(254);
            draft.Property(item => item.Version).IsConcurrencyToken();
            draft.HasOne<CatalogEntry>().WithMany().HasForeignKey(item => item.CategoryId).OnDelete(DeleteBehavior.Restrict);
            draft.HasOne<CatalogEntry>().WithMany().HasForeignKey(item => item.DistrictId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
