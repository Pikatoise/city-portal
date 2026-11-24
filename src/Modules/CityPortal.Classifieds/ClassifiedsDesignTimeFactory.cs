using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.Classifieds;

internal sealed class ClassifiedsDesignTimeFactory : IDesignTimeDbContextFactory<ClassifiedsDbContext>
{
    public ClassifiedsDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Host=localhost;Database=cityportal";
        return new ClassifiedsDbContext(new DbContextOptionsBuilder<ClassifiedsDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "classifieds")).Options);
    }
}
