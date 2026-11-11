using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quartz;
using RabbitMQ.Client;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString, string messagingConnectionString)
    {
        services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "notifications")));
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<AccountMailOptions>();
        services.AddScoped(provider => new AccountMailQueue(provider.GetRequiredService<NotificationsDbContext>(),
            provider.GetRequiredService<IDbContextOutbox>(), provider.GetRequiredService<IDataProtectionProvider>()));
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
                    && !(await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
                    && await context.Database.SqlQueryRaw<bool>("""
                        SELECT to_regclass('notifications_messaging.wolverine_outgoing_envelopes') IS NOT NULL
                            AND to_regclass('notifications_messaging.wolverine_incoming_envelopes') IS NOT NULL
                            AND to_regclass('notifications.qrtz_job_details') IS NOT NULL AS "Value"
                        """).SingleAsync(cancellationToken))
            .AddCheck<MessagingHealthCheck>("rabbitmq", tags: ["ready"], timeout: TimeSpan.FromSeconds(10));
        return services;
    }

    public static IServiceCollection AddAccountMailWorker(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.Configure<AccountMailOptions>(configuration.GetSection("Mail"));
        services.AddScoped(provider => new AccountMailDeliveryService(provider.GetRequiredService<NotificationsDbContext>(),
            provider.GetRequiredService<IDbContextOutbox>(), provider.GetRequiredService<IAccountMailLinkValidator>(),
            provider.GetRequiredService<IDataProtectionProvider>(), provider.GetRequiredService<IOptions<AccountMailOptions>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new EmailRetentionJob(provider.GetRequiredService<NotificationsDbContext>(), provider.GetRequiredService<TimeProvider>()));
        services.AddQuartz("CityPortal.Email", quartz =>
        {
            quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.ConfigureStore(options =>
                {
                    options.TablePrefix = "notifications.qrtz_";
                    options.SchemaProvisioning = SchemaProvisioning.Validate;
                    options.StoreJobDataAsStrings = true;
                });
                store.UseClustering();
            });
            var key = new JobKey("completed-email-retention", "notifications");
            quartz.AddJob<EmailRetentionJob>(job => job.WithIdentity(key).RequestRecovery());
            quartz.AddTrigger(trigger => trigger.WithIdentity("completed-email-retention-daily", "notifications").ForJob(key)
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(24)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)));
        });
        services.AddQuartzHostedService("CityPortal.Email", options => options.WaitForJobsToComplete = true);
        return services;
    }

    public static async Task MigrateNotificationsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        NotificationsDbContext context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
