using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.Notifications;

internal sealed class NotificationsDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=cityportal";
        var options = new DbContextOptionsBuilder<NotificationsDbContext>();
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"));
        return new NotificationsDbContext(options.Options);
    }
}
