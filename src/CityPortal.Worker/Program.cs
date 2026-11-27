using CityPortal.CitizenSubmissions;
using CityPortal.Identity;
using CityPortal.Integrations;
using CityPortal.Media;
using CityPortal.News;
using CityPortal.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CityPortal.Worker;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        string database = builder.Configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database must be configured.");
        string messaging = builder.Configuration.GetConnectionString("Messaging")
            ?? throw new InvalidOperationException("ConnectionStrings:Messaging must be configured.");

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        builder.Services.AddIdentityModule(database).AddNotificationsModule(database, messaging).AddMediaModule(database, builder.Configuration).AddNewsModule(database);
        builder.Services.AddCitizenSubmissionsModule(database);
        builder.Services.AddIntegrationsModule(database);
        builder.Services.AddWeatherWorker();
        builder.Services.AddTelegramIntegrations(builder.Configuration, builder.Environment.IsDevelopment());
        builder.Host.UseAccountMailMessaging(database, messaging, isWorker: true, options => options.ConfigureTelegramMessaging(isWorker: true));
        string keysPath = builder.Configuration["Security:DataProtectionKeysPath"]
            ?? throw new InvalidOperationException("Security:DataProtectionKeysPath must be configured.");
        builder.Services.AddDataProtection().SetApplicationName("CityPortal").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        builder.Services.AddAccountMailWorker(builder.Configuration, database, quartz =>
        {
            MediaModule.ConfigureMediaJobs(quartz, builder.Configuration);
            CitizenSubmissionsModule.ConfigureSuggestionJobs(quartz);
            WeatherModule.ConfigureWeatherJobs(quartz);
        });
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("CityPortal.Worker"))
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddMeter("CityPortal.Weather", "CityPortal.Telegram"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddOtlpExporter())
                .WithMetrics(metrics => metrics.AddOtlpExporter());
            builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());
        }

        await using WebApplication app = builder.Build();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") });
        await app.RunAsync();
    }
}
