using System.Threading.RateLimiting;
using CityPortal.Identity;
using CityPortal.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;

namespace CityPortal.Api;

internal static class NewsMcp
{
    private const string _endpoint = "/api/mcp";
    private const string _metadataEndpoint = "/.well-known/oauth-protected-resource" + _endpoint;
    private const string _scheme = "NewsMcpBearer";
    private const string _policy = "NewsMcpRead";
    private const string _scope = "news:read";
    private const int _bodyLimit = 16 * 1024;

    public static IServiceCollection AddNewsMcp(this IServiceCollection services, IConfiguration configuration)
    {
        var deployment = McpDeployment.Read(configuration);
        services.AddSingleton(deployment);
        services.AddSingleton(_ => PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1)
            })));
        if (deployment.Authority is null)
            return services;

        services.AddAuthentication().AddJwtBearer(_scheme, options =>
        {
            options.Authority = deployment.Authority;
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.SaveToken = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = deployment.Authority,
                ValidAudience = deployment.Resource,
                IgnoreTrailingSlashWhenValidatingAudience = false,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Challenge = $"Bearer resource_metadata=\"{deployment.Metadata}\"";
            options.Events.OnChallenge = context =>
            {
                if (context.AuthenticateFailure is not null)
                    context.Error = "invalid_token";
                return Task.CompletedTask;
            };
            options.Events.OnForbidden = context =>
            {
                context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{_scope}\", resource_metadata=\"{deployment.Metadata}\"";
                return Task.CompletedTask;
            };
        });
        services.AddAuthorizationBuilder().AddPolicy(_policy, policy =>
        {
            policy.AddAuthenticationSchemes(_scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context =>
                context.User.FindFirst("azp")?.Value is string clientId && deployment.ClientIds.Contains(clientId)
                && context.User.HasClaim(claim => claim.Type == "scope" && claim.Value.Split(' ').Contains(_scope, StringComparer.Ordinal)));
        });
        services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<NewsMcpTools>();
        return services;
    }

    public static void UseNewsMcp(this WebApplication app)
    {
        McpDeployment deployment = app.Services.GetRequiredService<McpDeployment>();
        PartitionedRateLimiter<HttpContext> limiter = app.Services.GetRequiredService<PartitionedRateLimiter<HttpContext>>();
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(_endpoint, StringComparison.OrdinalIgnoreCase)
                && !context.Request.Path.StartsWithSegments(_metadataEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            if (deployment.Authority is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            StringValues origins = context.Request.Headers.Origin;
            if (origins.Count > 0 && (origins.Count != 1 || !deployment.Origins.Contains(origins.ToString())))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            using RateLimitLease lease = limiter.AttemptAcquire(context);
            if (!lease.IsAcquired)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }

            IntegrationDbContext settings = context.RequestServices.GetRequiredService<IntegrationDbContext>();
            if (!await settings.Settings.AnyAsync(row => row.Id == 1 && row.McpEnabled, context.RequestAborted)
                || !(await context.RequestServices.GetRequiredService<SiteInstallation>().GetAsync(context.RequestAborted)).Installed)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (!HttpMethods.IsPost(context.Request.Method))
            {
                await next(context);
                return;
            }

            if (context.Request.ContentLength > _bodyLimit)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }
            Stream original = context.Request.Body;
            byte[] body = new byte[_bodyLimit + 1];
            int length = await original.ReadAtLeastAsync(body, body.Length, throwOnEndOfStream: false, cancellationToken: context.RequestAborted);
            if (length > _bodyLimit)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }
            using var buffered = new MemoryStream(body, 0, length, writable: false);
            context.Request.Body = buffered;
            try
            {
                await next(context);
            }
            finally
            {
                context.Request.Body = original;
            }
        });
    }

    public static void MapNewsMcp(this WebApplication app)
    {
        McpDeployment deployment = app.Services.GetRequiredService<McpDeployment>();
        if (deployment.Authority is not string authority)
            return;

        app.MapMcp(_endpoint).RequireAuthorization(_policy).ExcludeFromDescription();
        app.MapGet(_metadataEndpoint, () => TypedResults.Ok(new
        {
            resource = deployment.Resource,
            authorization_servers = new[] { authority },
            scopes_supported = new[] { _scope },
            bearer_methods_supported = new[] { "header" }
        })).AllowAnonymous().ExcludeFromDescription();
    }

    internal static string? PublicOrigin(IConfiguration configuration) =>
        IntegrationOrigin.FromConfiguration(configuration)?.GetLeftPart(UriPartial.Authority);

    private sealed record McpDeployment(string? Authority, string Resource, string Metadata, HashSet<string> ClientIds, HashSet<string> Origins)
    {
        public static McpDeployment Read(IConfiguration configuration)
        {
            string[] clientIds = configuration.GetSection("Mcp:AllowedClientIds").Get<string[]>() ?? [];
            var origins = new HashSet<string>(configuration.GetSection("Mcp:AllowedOrigins").Get<string[]>() ?? [], StringComparer.Ordinal);
            string? publicOrigin = PublicOrigin(configuration);
            if (publicOrigin is not null)
                origins.Add(publicOrigin);
            string? issuerOrigin = IntegrationOrigin.ParseHttps(configuration["Mcp:Authority"])?.GetLeftPart(UriPartial.Authority);
            string? authority = issuerOrigin is not null && publicOrigin is not null && clientIds.Any(id => !string.IsNullOrWhiteSpace(id))
                ? issuerOrigin + "/" : null;
            return new McpDeployment(authority, publicOrigin + _endpoint, publicOrigin + _metadataEndpoint, new HashSet<string>(clientIds, StringComparer.Ordinal), origins);
        }
    }
}
