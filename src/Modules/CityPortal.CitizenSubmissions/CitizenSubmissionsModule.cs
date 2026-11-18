using CityPortal.Identity;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace CityPortal.CitizenSubmissions;

public static class CitizenSubmissionsModule
{
    public static IServiceCollection AddCitizenSubmissionsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SuggestionsDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "citizen_submissions")));
        services.AddScoped(provider => new SuggestionOperations(provider.GetRequiredService<SuggestionsDbContext>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ArticleOperations>()));
        services.AddScoped(provider => new StaffSuggestionOperations(provider.GetRequiredService<SuggestionsDbContext>(),
            provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ArticleOperations>()));
        services.AddScoped<IArticleDraftDeletionObserver, SuggestionDraftDeletion>();
        services.AddScoped<IAccountRoleChangeObserver>(provider => provider.GetRequiredService<StaffSuggestionOperations>());
        services.AddScoped(provider => new SuggestionExpiryJob(provider.GetRequiredService<SuggestionsDbContext>(), provider.GetRequiredService<TimeProvider>()));
        services.AddHealthChecks().AddDbContextCheck<SuggestionsDbContext>("suggestions-database", tags: ["ready"],
            customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token) && !(await context.Database.GetPendingMigrationsAsync(token)).Any());
        return services;
    }

    public static void ConfigureSuggestionJobs(IQuartzBuilder quartz)
    {
        var key = new JobKey("suggestion-expiry", "citizen-submissions");
        quartz.AddJob<SuggestionExpiryJob>(job => job.WithIdentity(key).RequestRecovery());
        quartz.AddTrigger(trigger => trigger.WithIdentity("suggestion-expiry-interval", key.Group).ForJob(key)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)));
    }

    public static async Task MigrateCitizenSubmissionsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SuggestionsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
