using System.Diagnostics;
using CityPortal.Identity;
using CityPortal.Notifications;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace CityPortal.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        bool migrate = args.Contains("--migrate", StringComparer.Ordinal);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--migrate").ToArray());
        string database = builder.Configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database must be configured.");
        string messaging = builder.Configuration.GetConnectionString("Messaging")
            ?? throw new InvalidOperationException("ConnectionStrings:Messaging must be configured.");

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        builder.Services.AddIdentityModule(database).AddNotificationsModule(database, messaging);
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            context.ProblemDetails.Extensions["code"] = $"http_{context.ProblemDetails.Status ?? 500}";
        });
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("CityPortal.Api"))
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddOtlpExporter())
                .WithMetrics(metrics => metrics.AddOtlpExporter());
            builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());
        }

        await using WebApplication app = builder.Build();

        if (migrate)
        {
            await app.Services.MigrateIdentityAsync(CancellationToken.None);
            await app.Services.MigrateNotificationsAsync(CancellationToken.None);
            app.Logger.LogInformation("Applied Identity and Notifications database migrations.");
            return;
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapControllers();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        await app.RunAsync();
    }
}
