using Microsoft.EntityFrameworkCore;

namespace CityPortal.Media;

internal sealed class MediaDbContext : DbContext
{
    public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options) { }

    public DbSet<MediaImage> Images => Set<MediaImage>();
    public DbSet<ImageOutput> Outputs => Set<ImageOutput>();
    public DbSet<ImageReference> References => Set<ImageReference>();
    public DbSet<SuggestionImageReference> SuggestionImages => Set<SuggestionImageReference>();
    public DbSet<ImageTemplate> Templates => Set<ImageTemplate>();
    public DbSet<TemplateEvent> Events => Set<TemplateEvent>();
    public DbSet<TemplateRemovalRequest> RemovalRequests => Set<TemplateRemovalRequest>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("media");
        model.Entity<MediaImage>().ToTable("Images");
        model.Entity<MediaImage>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<MediaImage>().Property(x => x.State).HasMaxLength(20);
        model.Entity<MediaImage>().Property(x => x.ErrorCode).HasMaxLength(80);
        model.Entity<MediaImage>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<MediaImage>().HasIndex(x => new { x.OwnerId, x.CreatedAtUtc });
        model.Entity<MediaImage>().HasIndex(x => new { x.State, x.NextAttemptAtUtc });
        model.Entity<ImageOutput>().HasKey(x => new { x.ImageId, x.Variant });
        model.Entity<ImageOutput>().Property(x => x.Variant).HasMaxLength(10);
        model.Entity<ImageOutput>().HasOne<MediaImage>().WithMany().HasForeignKey(x => x.ImageId);
        model.Entity<ImageReference>().HasKey(x => new { x.RevisionId, x.ImageId });
        model.Entity<ImageReference>().Property(x => x.Current).HasDefaultValue(true);
        model.Entity<ImageReference>().Property(x => x.AuthorVisible).HasDefaultValue(true);
        model.Entity<ImageReference>().HasIndex(x => new { x.ImageId, x.Public });
        model.Entity<ImageReference>().HasOne<MediaImage>().WithMany().HasForeignKey(x => x.ImageId);
        model.Entity<SuggestionImageReference>().HasKey(item => item.ImageId);
        model.Entity<SuggestionImageReference>().HasIndex(item => item.SuggestionId);
        model.Entity<SuggestionImageReference>().HasOne<MediaImage>().WithMany().HasForeignKey(item => item.ImageId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ImageTemplate>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<ImageTemplate>().Property(x => x.Approval).HasMaxLength(20);
        model.Entity<ImageTemplate>().Property(x => x.Reason).HasMaxLength(2000);
        model.Entity<ImageTemplate>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<ImageTemplate>().HasIndex(x => new { x.Approval, x.Submitted, x.CreatedAtUtc });
        model.Entity<ImageTemplate>().HasIndex(x => x.ImageId);
        model.Entity<ImageTemplate>().HasOne<MediaImage>().WithMany().HasForeignKey(x => x.ImageId);
        model.Entity<TemplateEvent>().Property(x => x.Action).HasMaxLength(40);
        model.Entity<TemplateEvent>().Property(x => x.Approval).HasMaxLength(20);
        model.Entity<TemplateEvent>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<TemplateEvent>().Property(x => x.Reason).HasMaxLength(2000);
        model.Entity<TemplateEvent>().HasIndex(x => new { x.TemplateId, x.CreatedAtUtc });
        model.Entity<TemplateEvent>().HasOne<ImageTemplate>().WithMany().HasForeignKey(x => x.TemplateId);
        model.Entity<TemplateEvent>().HasOne<MediaImage>().WithMany().HasForeignKey(x => x.ImageId);
        model.Entity<TemplateRemovalRequest>().HasIndex(x => x.TemplateId).IsUnique().HasFilter("\"ResolvedAtUtc\" IS NULL");
        model.Entity<TemplateRemovalRequest>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
        model.Entity<TemplateRemovalRequest>().HasOne<ImageTemplate>().WithMany().HasForeignKey(x => x.TemplateId);
    }
}
