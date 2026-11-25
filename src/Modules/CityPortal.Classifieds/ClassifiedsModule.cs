using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.Classifieds;

public static class ClassifiedsModule
{
    public static IServiceCollection AddClassifiedsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ClassifiedsDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "classifieds")));
        services.AddScoped(provider => new ClassifiedsCatalog(provider.GetRequiredService<ClassifiedsDbContext>(),
            provider.GetRequiredService<AccountAuthorization>()));
        services.AddScoped(provider => new ListingDraftOperations(provider.GetRequiredService<ClassifiedsDbContext>(),
            provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ListingImages>()));
        services.AddScoped<ListingNotifications>();
        services.AddScoped(provider => new ListingHistory(provider.GetRequiredService<ClassifiedsDbContext>(),
            provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<AccountOperations>()));
        services.AddScoped(provider => new ListingReviewOperations(provider.GetRequiredService<ClassifiedsDbContext>(),
            provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ListingImages>(),
            provider.GetRequiredService<ListingNotifications>()));
        services.AddScoped(provider => new ListingPublications(provider.GetRequiredService<ClassifiedsDbContext>(),
            provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<ListingImages>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ListingNotifications>()));
        services.AddScoped<IReportTarget, ListingReportTarget>();
        services.AddScoped<IAccountBlockObserver>(provider => provider.GetRequiredService<ListingPublications>());
        services.AddScoped(provider => new PublicListingSearch(provider.GetRequiredService<ClassifiedsDbContext>(), provider.GetRequiredService<TimeProvider>()));
        services.AddHealthChecks().AddDbContextCheck<ClassifiedsDbContext>("classifieds-database", tags: ["ready"],
            customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token)
                && !(await context.Database.GetPendingMigrationsAsync(token)).Any());
        return services;
    }

    public static async Task MigrateClassifiedsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ClassifiedsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
