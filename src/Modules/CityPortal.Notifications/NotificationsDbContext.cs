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
    public DbSet<MailConfiguration> MailConfigurations => Set<MailConfiguration>();
    public DbSet<AccountNotification> AccountNotifications => Set<AccountNotification>();
    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();
    public DbSet<WorkflowReceipt> WorkflowReceipts => Set<WorkflowReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
        modelBuilder.Entity<MailConfiguration>().ToTable("MailConfiguration");
        modelBuilder.Entity<NotificationPreference>().HasKey(item => new { item.AccountId, item.Type });
        modelBuilder.Entity<NotificationPreference>().Property(item => item.Type).HasMaxLength(40);
        modelBuilder.Entity<WorkflowReceipt>().HasKey(item => new { item.EventId, item.AccountId, item.Type });
        modelBuilder.Entity<WorkflowReceipt>().Property(item => item.Type).HasMaxLength(40);
        modelBuilder.Entity<AccountNotification>().Property(item => item.Type).HasMaxLength(40);
        modelBuilder.Entity<AccountNotification>().HasIndex(item => new { item.AccountId, item.CreatedAtUtc });
        modelBuilder.Entity<AccountNotification>().HasIndex(item => item.CreatedAtUtc);
        modelBuilder.Entity<EmailDelivery>(entity =>
        {
            entity.ToTable("EmailDeliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Purpose).HasMaxLength(32);
            entity.Property(x => x.NotificationType).HasMaxLength(40);
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
