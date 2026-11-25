using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.Reports;

internal sealed class ReportsDesignTimeFactory : IDesignTimeDbContextFactory<ReportsDbContext>
{
    public ReportsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ReportsDbContext> options = new DbContextOptionsBuilder<ReportsDbContext>().UseNpgsql(
            "Host=localhost;Database=cityportal;Username=cityportal",
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "reports"));
        return new ReportsDbContext(options.Options);
    }
}
