using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.Reports;

public static class ReportsModule
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ReportsDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "reports")));
        services.AddScoped(provider => new ReportOperations(provider.GetRequiredService<ReportsDbContext>(),
            provider.GetRequiredService<AccountAuthorization>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetServices<IReportTarget>()));
        services.AddHealthChecks().AddDbContextCheck<ReportsDbContext>("reports-database", tags: ["ready"],
            customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token)
                && !(await context.Database.GetPendingMigrationsAsync(token)).Any());
        return services;
    }

    public static async Task MigrateReportsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
