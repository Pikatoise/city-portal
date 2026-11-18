using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace CityPortal.Media;

public static class MediaModule
{
    public static IServiceCollection AddMediaModule(this IServiceCollection services, string database, IConfiguration configuration)
    {
        services.Configure<MediaStorageOptions>(configuration.GetSection("Media"));
        services.AddDbContext<MediaDbContext>(options => options.UseNpgsql(database, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "media")));
        services.AddSingleton<MediaImageProcessor>();
        services.AddSingleton<MediaObjectStore>();
        services.AddScoped<MediaReferences>();
        services.AddScoped(provider => new SuggestionImages(provider.GetRequiredService<MediaDbContext>(), provider.GetRequiredService<MediaObjectStore>));
        services.AddScoped(provider => new MediaOperations(provider.GetRequiredService<MediaDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<MediaImageProcessor>(), provider.GetRequiredService<MediaObjectStore>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new TemplateNotifications(provider.GetRequiredService<MediaDbContext>(), provider.GetRequiredService<WorkflowNotificationQueue>(), provider.GetRequiredService<AccountOperations>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new TemplateOperations(provider.GetRequiredService<MediaDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<TemplateNotifications>()));
        services.AddScoped(provider => new MediaMaintenance(provider.GetRequiredService<MediaDbContext>(), provider.GetRequiredService<MediaObjectStore>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<MediaMaintenance>>()));
        // Stage 1 installations remain usable while Media is being introduced. Configured deployments require both checks.
        if (!string.IsNullOrWhiteSpace(configuration["Media:Endpoint"]))
        {
            services.AddHealthChecks().AddDbContextCheck<MediaDbContext>("media-database", tags: ["ready"], customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token) && !(await context.Database.GetPendingMigrationsAsync(token)).Any());
            services.AddHealthChecks().AddCheck<MediaStorageHealthCheck>("media-storage", tags: ["ready"]);
        }
        return services;
    }

    public static void ConfigureMediaJobs(IQuartzBuilder quartz, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["Media:Endpoint"]))
            return;
        var key = new JobKey("media-reconciliation", "media");
        quartz.AddJob<MediaMaintenanceJob>(job => job.WithIdentity(key).RequestRecovery());
        quartz.AddTrigger(trigger => trigger.WithIdentity("media-reconciliation-interval", "media").ForJob(key)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)));
    }

    public static async Task MigrateMediaAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediaDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
