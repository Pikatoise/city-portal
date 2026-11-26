using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.Integrations;

public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services, string database)
    {
        services.AddDbContext<IntegrationDbContext>(options => options.UseNpgsql(database,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "integrations")));
        services.AddScoped<IntegrationAdministration>();
        services.AddHealthChecks().AddDbContextCheck<IntegrationDbContext>("integrations-database", tags: ["ready"],
            customTestQuery: async (context, token) => await context.Database.CanConnectAsync(token)
                && !(await context.Database.GetPendingMigrationsAsync(token)).Any());
        return services;
    }

    public static async Task MigrateIntegrationsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntegrationDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
