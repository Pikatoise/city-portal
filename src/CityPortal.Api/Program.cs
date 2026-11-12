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
        int bootstrapIndex = Array.IndexOf(args, "--bootstrap-admin");
        string? bootstrapEmail = bootstrapIndex >= 0 && bootstrapIndex + 1 < args.Length ? args[bootstrapIndex + 1] : null;
        if (bootstrapIndex >= 0 && (string.IsNullOrWhiteSpace(bootstrapEmail) || bootstrapEmail.StartsWith("--", StringComparison.Ordinal) || migrate))
            throw new ArgumentException("Use --bootstrap-admin <confirmed-account-email> separately from --migrate.");
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args.Where((argument, index) => argument != "--migrate" && index != bootstrapIndex && (bootstrapIndex < 0 || index != bootstrapIndex + 1)).ToArray());
        string database = builder.Configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database must be configured.");
        string messaging = builder.Configuration.GetConnectionString("Messaging")
            ?? throw new InvalidOperationException("ConnectionStrings:Messaging must be configured.");

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        string timeZoneId = builder.Configuration["City:TimeZone"] ?? "UTC";
        if (timeZoneId != "UTC" && TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out string? ianaId))
            timeZoneId = ianaId;
        builder.Services.AddSingleton(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        builder.Services.AddIdentityModule(database).AddNotificationsModule(database, messaging);
        builder.Host.UseAccountMailMessaging(database, messaging, isWorker: false);
        builder.Services.AddAccountSecurity(builder.Configuration);
        builder.Services.AddControllers(options => options.Filters.Add<AccountRequestFilter>());
        builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            // The generated client must send a concrete content type instead of the JSON formatter's wildcard alias.
            if (operation.RequestBody?.Content is { } content && content.ContainsKey("application/json"))
                foreach (string mediaType in content.Keys.Where(key => key != "application/json").ToArray())
                    content.Remove(mediaType);
            return Task.CompletedTask;
        }));
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

        if (bootstrapEmail is not null)
        {
            await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<AccountAdministration>().BootstrapAsync(bootstrapEmail, CancellationToken.None);
                app.Logger.LogInformation("Provisioned the initial administrator.");
            }
            catch (AccountOperationException exception)
            {
                app.Logger.LogError("Administrator setup refused: {Code}.", exception.Code);
                Environment.ExitCode = 1;
            }
            return;
        }

        if (migrate)
        {
            await app.Services.MigrateIdentityAsync(CancellationToken.None);
            await app.Services.MigrateNotificationsAsync(CancellationToken.None);
            await app.Services.MigrateAccountMailMessagingAsync();
            app.Logger.LogInformation("Applied Identity and Notifications database migrations.");
            return;
        }

        app.UseExceptionHandler();
        app.UseMiddleware<AccountErrorMiddleware>();
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/api/account", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
            }
            await next(context);
        });
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
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
