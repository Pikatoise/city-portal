using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPortal.Comments;

internal sealed class CommentsDesignTimeFactory : IDesignTimeDbContextFactory<CommentsDbContext>
{
    public CommentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CommentsDbContext>().UseNpgsql(
            "Host=localhost;Database=cityportal;Username=cityportal",
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "comments")).Options);
}
