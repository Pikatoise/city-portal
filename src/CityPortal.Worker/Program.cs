using CityPortal.Identity;
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
        builder.Services.AddIdentityModule(database).AddNotificationsModule(database, messaging);
        builder.Host.UseAccountMailMessaging(database, messaging, isWorker: true);
        string keysPath = builder.Configuration["Security:DataProtectionKeysPath"]
            ?? throw new InvalidOperationException("Security:DataProtectionKeysPath must be configured.");
        builder.Services.AddDataProtection().SetApplicationName("CityPortal").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        builder.Services.AddAccountMailWorker(builder.Configuration, database);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("CityPortal.Worker"))
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

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
