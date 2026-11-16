using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.News;

public static class NewsModule
{
    public static IServiceCollection AddNewsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<NewsDbContext>(options => options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "news")));
        services.AddScoped(provider => new ArticleOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<MediaReferences>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ReviewOperations>()));
        services.AddScoped(provider => new ReviewOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<AccountOperations>(), provider.GetRequiredService<MediaReferences>(), provider.GetRequiredService<WorkflowNotificationQueue>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new PublicArticleOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountOperations>()));
        services.AddScoped<IAccountRoleChangeObserver>(provider => provider.GetRequiredService<ReviewOperations>());
        services.AddScoped(provider => new TaxonomyOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>()));
        services.AddHealthChecks().AddDbContextCheck<NewsDbContext>("news-database", tags: ["ready"], customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token) && !(await context.Database.GetPendingMigrationsAsync(token)).Any());
        return services;
    }

    public static async Task MigrateNewsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NewsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
