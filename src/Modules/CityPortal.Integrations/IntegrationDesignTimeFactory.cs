using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.Integrations;

internal sealed class IntegrationDesignTimeFactory : IDesignTimeDbContextFactory<IntegrationDbContext>
{
    public IntegrationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IntegrationDbContext>().UseNpgsql(
            "Host=localhost;Database=cityportal;Username=cityportal",
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "integrations")).Options);
}
