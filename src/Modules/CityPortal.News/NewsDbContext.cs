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
    public DbSet<HomepageSelection> HomepageSelections => Set<HomepageSelection>();
    public DbSet<CurationChange> CurationChanges => Set<CurationChange>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("news");
        model.Entity<HomepageSelection>().Property(item => item.Version).IsConcurrencyToken();
        model.Entity<HomepageSelection>().HasData(new HomepageSelection());
        model.Entity<CurationChange>().Property(item => item.SelectionJson).HasColumnType("jsonb");
        model.Entity<CurationChange>().HasIndex(item => item.CreatedAtUtc);
        model.Entity<NewsArticle>().Property(item => item.Version).IsConcurrencyToken();
        model.Entity<NewsArticle>().Property(item => item.PublicationState).HasMaxLength(32);
        model.Entity<NewsArticle>().HasIndex(item => new { item.AuthorId, item.UpdatedAtUtc });
        model.Entity<NewsArticle>().HasIndex(item => item.ReviewerId);
        // Public listing reads only live publications in first-publication order.
        model.Entity<NewsArticle>().HasIndex(item => new { item.FirstPublishedAtUtc, item.Id }).IsDescending(true, false)
            .HasFilter("\"PublicationState\" = 'Published' AND \"DeletedAtUtc\" IS NULL");
        model.Entity<ArticleRevision>().HasIndex(item => new { item.ArticleId, item.SubmittedAtUtc });
        model.Entity<ArticleRevision>().HasOne<NewsArticle>().WithMany().HasForeignKey(item => item.ArticleId);
        model.Entity<ArticleRevision>().Property(item => item.ContentJson).HasColumnType("jsonb");
        model.Entity<ArticleRevision>().Property(item => item.Title).HasMaxLength(200);
        model.Entity<ArticleRevision>().Property(item => item.State).HasMaxLength(32);
        // Discovery projects and filters revision content through generated columns, so every revision write stays in sync.
        model.Entity<ArticleRevision>().Property(item => item.SearchVector).HasComputedColumnSql(
            """setweight(to_tsvector('russian', coalesce("Title", '')), 'A') || setweight(to_tsvector('russian', coalesce("ContentJson" ->> 'Summary', '')), 'B') || setweight(to_tsvector('russian', coalesce("PlainText", '')), 'C')""", stored: true);
        model.Entity<ArticleRevision>().Property(item => item.Summary).HasComputedColumnSql("""coalesce("ContentJson" ->> 'Summary', '')""", stored: true);
        model.Entity<ArticleRevision>().Property(item => item.CategoryId).HasComputedColumnSql("""("ContentJson" ->> 'CategoryId')::uuid""", stored: true);
        model.Entity<ArticleRevision>().Property(item => item.Tags).HasColumnType("jsonb").HasComputedColumnSql("""coalesce("ContentJson" -> 'TagIds', '[]')""", stored: true);
        model.Entity<ArticleRevision>().Property(item => item.CoverMediaId).HasComputedColumnSql("""("ContentJson" -> 'Cover' ->> 'MediaId')::uuid""", stored: true);
        model.Entity<ArticleRevision>().Property(item => item.CoverAlt).HasComputedColumnSql("""coalesce("ContentJson" -> 'Cover' ->> 'Alt', '')""", stored: true);
        model.Entity<ArticleRevision>().HasIndex(item => item.SearchVector).HasMethod("GIN");
        model.Entity<ArticleRevision>().HasIndex(item => item.CategoryId);
        model.Entity<ArticleRevision>().HasIndex(item => item.Tags).HasMethod("GIN");
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
