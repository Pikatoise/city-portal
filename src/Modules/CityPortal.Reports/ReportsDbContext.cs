using Microsoft.EntityFrameworkCore;

namespace CityPortal.Reports;

internal sealed class ReportsDbContext : DbContext
{
    public ReportsDbContext(DbContextOptions<ReportsDbContext> options) : base(options)
    {
    }

    internal DbSet<ReportedContent> Targets => Set<ReportedContent>();

    internal DbSet<ContentReport> Reports => Set<ContentReport>();

    internal DbSet<ReportDecision> Decisions => Set<ReportDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reports");
        modelBuilder.Entity<ReportedContent>(target =>
        {
            target.HasKey(item => new { item.Kind, item.Id });
            target.Property(item => item.Kind).HasMaxLength(12);
            target.Property(item => item.Title).HasMaxLength(200);
            target.Property(item => item.SnapshotText).HasMaxLength(500);
            target.HasOne<ReportDecision>().WithMany().HasForeignKey(item => item.DirectDecisionId).OnDelete(DeleteBehavior.Restrict);
            target.HasMany(item => item.Reports).WithOne().HasForeignKey(item => new { item.Kind, item.TargetId }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ContentReport>(report =>
        {
            report.HasKey(item => item.Sequence);
            report.Property(item => item.Sequence).UseIdentityAlwaysColumn();
            report.Property(item => item.Kind).HasMaxLength(12);
            report.Property(item => item.Category).HasMaxLength(40);
            report.Property(item => item.Text).HasMaxLength(500);
            report.HasIndex(item => new { item.Kind, item.TargetId, item.Sequence });
            report.HasOne<ReportDecision>().WithMany().HasForeignKey(item => item.DecisionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ReportDecision>(decision =>
        {
            decision.Property(item => item.Outcome).HasMaxLength(10);
            decision.Property(item => item.Reason).HasMaxLength(500);
        });
    }
}
