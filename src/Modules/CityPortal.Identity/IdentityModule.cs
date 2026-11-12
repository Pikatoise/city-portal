using CityPortal.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CityPortal.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped(provider => new AccountOperations(provider.GetRequiredService<IdentityDbContext>(), provider.GetRequiredService<AccountMailQueue>(), provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IAccountMailLinkValidator, AccountMailLinkValidator>();
        services.AddScoped(provider => new AccountAdministration(provider.GetRequiredService<IdentityDbContext>(), provider.GetRequiredService<EmailAdministration>(), provider.GetRequiredService<TimeProvider>()));
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
