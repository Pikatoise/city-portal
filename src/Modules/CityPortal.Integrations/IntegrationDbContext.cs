using Microsoft.EntityFrameworkCore;

namespace CityPortal.Integrations;

public sealed class IntegrationDbContext : DbContext
{
    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : base(options)
    {
    }

    public DbSet<IntegrationSettings> Settings => Set<IntegrationSettings>();

    public DbSet<WeatherSnapshot> WeatherSnapshots => Set<WeatherSnapshot>();

    internal DbSet<IntegrationSettingsChange> SettingsChanges => Set<IntegrationSettingsChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("integrations");
        modelBuilder.Entity<IntegrationSettings>(entity =>
        {
            entity.ToTable("settings", table => table.HasCheckConstraint("CK_settings_singleton", "\"Id\" = 1"));
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.Property(item => item.TelegramChannel).HasMaxLength(33);
            entity.HasData(new IntegrationSettings());
        });
        modelBuilder.Entity<WeatherSnapshot>().ToTable("weather_snapshots",
            table => table.HasCheckConstraint("CK_weather_snapshots_singleton", "\"Id\" = 1"));
        modelBuilder.Entity<IntegrationSettingsChange>(entity =>
        {
            entity.ToTable("settings_changes");
            entity.HasKey(item => item.Version);
            entity.Property(item => item.Version).ValueGeneratedNever();
            entity.Property(item => item.Settings).HasColumnType("jsonb");
        });
        TelegramDelivery.Configure(modelBuilder);
    }
}
