using Microsoft.EntityFrameworkCore;

namespace CityPortal.Comments;

internal sealed class CommentsDbContext : DbContext
{
    public CommentsDbContext(DbContextOptions<CommentsDbContext> options) : base(options)
    {
    }

    internal DbSet<ArticleComment> Comments => Set<ArticleComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("comments");
        modelBuilder.Entity<ArticleComment>(comment =>
        {
            comment.Property(item => item.Body).HasMaxLength(500);
            comment.HasIndex(item => new { item.ArticleId, item.CreatedAtUtc, item.Id }).IsDescending(false, true, true)
                .HasFilter("\"DeletedAtUtc\" IS NULL AND \"HiddenAtUtc\" IS NULL");
            comment.HasIndex(item => new { item.OwnerId, item.CreatedAtUtc, item.Id }).IsDescending(false, true, true)
                .HasFilter("\"OwnerId\" IS NOT NULL AND \"DeletedAtUtc\" IS NULL");
        });
    }
}
