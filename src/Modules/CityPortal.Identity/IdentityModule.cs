using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
        services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("identity-database", tags: ["ready"],
            customTestQuery: async (context, cancellationToken) =>
                await context.Database.CanConnectAsync(cancellationToken)
                && !(await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any());
        return services;
    }

    public static async Task MigrateIdentityAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
