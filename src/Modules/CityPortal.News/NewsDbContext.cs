using Microsoft.EntityFrameworkCore;

namespace CityPortal.News;

internal sealed class NewsDbContext : DbContext
{
    public NewsDbContext(DbContextOptions<NewsDbContext> options) : base(options)
    {
    }

    public DbSet<NewsArticle> Articles => Set<NewsArticle>();
    public DbSet<ArticleRevision> Revisions => Set<ArticleRevision>();
    public DbSet<TaxonomyEntry> Taxonomy => Set<TaxonomyEntry>();
    public DbSet<TaxonomyChange> TaxonomyChanges => Set<TaxonomyChange>();
    public DbSet<ArticleHistory> History => Set<ArticleHistory>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("news");
        model.Entity<NewsArticle>().Property(item => item.Version).IsConcurrencyToken();
        model.Entity<NewsArticle>().Property(item => item.PublicationState).HasMaxLength(32);
        model.Entity<NewsArticle>().HasIndex(item => new { item.AuthorId, item.UpdatedAtUtc });
        model.Entity<NewsArticle>().HasIndex(item => item.ReviewerId);
        model.Entity<ArticleRevision>().HasIndex(item => new { item.ArticleId, item.SubmittedAtUtc });
        model.Entity<ArticleRevision>().HasOne<NewsArticle>().WithMany().HasForeignKey(item => item.ArticleId);
        model.Entity<ArticleRevision>().Property(item => item.ContentJson).HasColumnType("jsonb");
        model.Entity<ArticleRevision>().Property(item => item.Title).HasMaxLength(200);
        model.Entity<ArticleRevision>().Property(item => item.State).HasMaxLength(32);
        model.Entity<ArticleHistory>().HasOne<NewsArticle>().WithMany().HasForeignKey(item => item.ArticleId);
        model.Entity<ArticleHistory>().HasOne<ArticleRevision>().WithMany().HasForeignKey(item => item.RevisionId);
        model.Entity<ArticleHistory>().HasIndex(item => new { item.ArticleId, item.CreatedAtUtc });
        model.Entity<ArticleHistory>().Property(item => item.Action).HasMaxLength(40);
        model.Entity<ArticleHistory>().Property(item => item.Reason).HasMaxLength(2000);
        model.Entity<TaxonomyEntry>().Property(item => item.Kind).HasMaxLength(20);
        model.Entity<TaxonomyEntry>().Property(item => item.Name).HasMaxLength(100);
        model.Entity<TaxonomyEntry>().Property(item => item.NormalizedName).HasMaxLength(300);
        model.Entity<TaxonomyEntry>().HasIndex(item => new { item.Kind, item.NormalizedName }).IsUnique();
        model.Entity<TaxonomyEntry>().Property(item => item.Version).IsConcurrencyToken();
        model.Entity<TaxonomyChange>().HasOne<TaxonomyEntry>().WithMany().HasForeignKey(item => item.TaxonomyId);
        model.Entity<TaxonomyChange>().Property(item => item.Name).HasMaxLength(100);
        model.Entity<TaxonomyChange>().Property(item => item.Action).HasMaxLength(20);
        model.Entity<TaxonomyChange>().HasIndex(item => new { item.TaxonomyId, item.Version }).IsUnique();
    }
}
