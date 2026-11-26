using CityPortal.Identity;
using CityPortal.News;
using CityPortal.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.Comments;

public static class CommentsModule
{
    public static IServiceCollection AddCommentsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CommentsDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "comments")));
        services.AddScoped(provider => new CommentOperations(provider.GetRequiredService<CommentsDbContext>(),
            provider.GetRequiredService<PublicArticleOperations>(), provider.GetRequiredService<AccountOperations>(),
            provider.GetRequiredService<AccountAuthorization>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IReportTarget, CommentReportTarget>();
        services.AddHealthChecks().AddDbContextCheck<CommentsDbContext>("comments-database", tags: ["ready"],
            customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token)
                && !(await context.Database.GetPendingMigrationsAsync(token)).Any());
        return services;
    }

    public static async Task MigrateCommentsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommentsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
