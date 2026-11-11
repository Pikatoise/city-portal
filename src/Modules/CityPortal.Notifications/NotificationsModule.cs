using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace CityPortal.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString, string messagingConnectionString)
    {
        services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "notifications")));
        services.AddSingleton(new ConnectionFactory
        {
            Uri = new Uri(messagingConnectionString),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            AutomaticRecoveryEnabled = false
        });
        services.AddHealthChecks()
            .AddDbContextCheck<NotificationsDbContext>("notifications-database", tags: ["ready"],
                customTestQuery: async (context, cancellationToken) =>
                    await context.Database.CanConnectAsync(cancellationToken)
                    && !(await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            .AddCheck<MessagingHealthCheck>("rabbitmq", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));
        return services;
    }

    public static async Task MigrateNotificationsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        NotificationsDbContext context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
