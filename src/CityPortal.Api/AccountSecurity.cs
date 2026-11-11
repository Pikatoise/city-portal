using System.Net;
using System.Security.Claims;
using CityPortal.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace CityPortal.Api;

internal static class AccountSecurity
{
    public static IServiceCollection AddAccountSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        IDataProtectionBuilder protection = services.AddDataProtection().SetApplicationName("CityPortal");
        string? keys = configuration["Security:DataProtectionKeysPath"];
        if (!string.IsNullOrWhiteSpace(keys))
            protection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-CityPortal.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "__Host-CityPortal.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirstValue("session"), out Guid sessionId))
                {
                    context.RejectPrincipal();
                    return;
                }
                AccountResponse? account = await context.HttpContext.RequestServices.GetRequiredService<AccountOperations>().FindSessionAsync(sessionId, context.HttpContext.RequestAborted);
                if (account is null)
                {
                    context.RejectPrincipal();
                    return;
                }
                var claims = new List<Claim> { new("session", sessionId.ToString()), new(ClaimTypes.NameIdentifier, account.Id.ToString()), new("email_confirmed", account.EmailConfirmed.ToString()) };
                claims.AddRange(account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
                context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            };
        });
        services.AddAuthorization();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            string[] proxies = configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? [];
            options.ForwardedHeaders = proxies.Length == 0 ? ForwardedHeaders.None : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (string proxy in proxies)
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        return services;
    }
}
