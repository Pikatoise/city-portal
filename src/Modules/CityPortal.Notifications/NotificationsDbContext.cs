using Microsoft.EntityFrameworkCore;

namespace CityPortal.Notifications;

internal sealed class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
    }
}
