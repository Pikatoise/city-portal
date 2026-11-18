using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.CitizenSubmissions;

internal sealed class SuggestionsDesignTimeFactory : IDesignTimeDbContextFactory<SuggestionsDbContext>
{
    public SuggestionsDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Host=localhost;Database=cityportal";
        return new SuggestionsDbContext(new DbContextOptionsBuilder<SuggestionsDbContext>()
            .UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "citizen_submissions")).Options);
    }
}
