using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.News;

public static class NewsModule
{
    public static IServiceCollection AddNewsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<NewsDbContext>(options => options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "news")));
        services.AddScoped(provider => new ArticleOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<MediaReferences>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ReviewOperations>()));
        services.AddScoped(provider => new ReviewOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<AccountOperations>(), provider.GetRequiredService<MediaReferences>(), provider.GetRequiredService<WorkflowNotificationQueue>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<SiteInstallation>(), provider.GetRequiredService<TimeZoneInfo>()));
        services.AddScoped(provider => new PublicArticleOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountOperations>()));
        services.AddScoped(provider => new PublicNewsDiscoveryOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<SiteInstallation>(), provider.GetRequiredService<TimeZoneInfo>(), provider.GetRequiredService<AccountOperations>()));
        services.AddScoped(provider => new CurationOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IAccountRoleChangeObserver>(provider => provider.GetRequiredService<ReviewOperations>());
        services.AddScoped(provider => new TaxonomyOperations(provider.GetRequiredService<NewsDbContext>(), provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>()));
        services.AddHealthChecks().AddDbContextCheck<NewsDbContext>("news-database", tags: ["ready"], customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token) && !(await context.Database.GetPendingMigrationsAsync(token)).Any() && !await context.Articles.AnyAsync(item => item.FirstPublishedAtUtc != null && (item.PublicAddress == null || item.FirstPublishedLocalDate == null), token));
        return services;
    }

    public static async Task MigrateNewsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        NewsDbContext context = scope.ServiceProvider.GetRequiredService<NewsDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        if (!await context.Articles.AnyAsync(item => item.FirstPublishedAtUtc != null && (item.PublicAddress == null || item.FirstPublishedLocalDate == null), cancellationToken))
            return;
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("LOCK TABLE news.\"Articles\" IN ACCESS EXCLUSIVE MODE", cancellationToken);
        InstallationInfo site = await scope.ServiceProvider.GetRequiredService<SiteInstallation>().GetAsync(cancellationToken);
        TimeZoneInfo zone = site.TimeZone is null ? scope.ServiceProvider.GetRequiredService<TimeZoneInfo>() : TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone);
        var rows = await (from article in context.Articles
                          join revision in context.Revisions on article.PublishedRevisionId equals revision.Id
                          where article.FirstPublishedAtUtc != null && (article.PublicAddress == null || article.FirstPublishedLocalDate == null)
                          select new { Article = article, revision.Title }).ToArrayAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Article.FirstPublishedLocalDate ??= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.Article.FirstPublishedAtUtc!.Value, zone).DateTime);
            row.Article.PublicAddress ??= ArticleUrls.Address(row.Title, row.Article.FirstPublishedLocalDate.Value, row.Article.Id);
        }
        await context.SaveChangesAsync(cancellationToken);
        if (await context.Articles.AnyAsync(item => item.FirstPublishedAtUtc != null && (item.PublicAddress == null || item.FirstPublishedLocalDate == null), cancellationToken))
            throw new InvalidOperationException("A published article could not be assigned an address.");
        await transaction.CommitAsync(cancellationToken);
    }
}
