using Microsoft.EntityFrameworkCore;

namespace CityPortal.Notifications;

internal sealed class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options)
    {
    }

    public DbSet<EmailDelivery> Deliveries => Set<EmailDelivery>();

    public DbSet<EmailAttempt> Attempts => Set<EmailAttempt>();

    public DbSet<EmailReplay> Replays => Set<EmailReplay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
        modelBuilder.Entity<EmailDelivery>(entity =>
        {
            entity.ToTable("EmailDeliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Purpose).HasMaxLength(32);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasMaxLength(80);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => new { x.State, x.NextAttemptAtUtc });
            entity.HasIndex(x => x.CompletedAtUtc);
        });
        modelBuilder.Entity<EmailAttempt>(entity =>
        {
            entity.ToTable("EmailAttempts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Result).HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasMaxLength(80);
            entity.HasOne<EmailDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.DeliveryId, x.Number }).IsUnique();
        });
        // Audit records survive deletion of completed delivery records.
        modelBuilder.Entity<EmailReplay>().HasIndex(replay => new { replay.DeliveryId, replay.CreatedAtUtc });
    }
}
