using Microsoft.EntityFrameworkCore;

namespace CityPortal.Integrations;

internal sealed class TelegramDelivery
{
    public Guid Id { get; set; }

    public Guid ArticleId { get; set; }

    public Guid RevisionId { get; set; }

    public string Channel { get; set; } = "";

    public string Text { get; set; } = "";

    public string State { get; set; } = "Queued";

    public long Version { get; set; } = 1;

    public int Attempt { get; set; }

    public int RateLimitRetries { get; set; }

    public int? MessageId { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    internal static void Configure(ModelBuilder model)
    {
        model.Entity<TelegramDelivery>(delivery =>
        {
            delivery.ToTable("TelegramDeliveries");
            delivery.HasIndex(item => new { item.ArticleId, item.Channel }).IsUnique();
            delivery.HasIndex(item => new { item.State, item.UpdatedAtUtc });
            delivery.Property(item => item.Channel).HasMaxLength(33);
            delivery.Property(item => item.Text).HasMaxLength(3000);
            delivery.Property(item => item.State).HasMaxLength(24);
            delivery.Property(item => item.ErrorCode).HasMaxLength(64);
        });
        model.Entity<TelegramDeliveryEvent>(entry =>
        {
            entry.ToTable("TelegramDeliveryEvents");
            entry.HasOne<TelegramDelivery>().WithMany().HasForeignKey(item => item.DeliveryId);
            entry.Property(item => item.Action).HasMaxLength(24);
        });
    }
}
