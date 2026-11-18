using System.Security.Claims;
using CityPortal.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CityPortal.Api;

internal sealed class AccountRequestFilter : IAsyncResourceFilter
{
    private static readonly SemaphoreSlim _mediaUpload = new(1);
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (http.Request.Path.StartsWithSegments("/api/news"))
            http.Response.Headers.CacheControl = "no-store";
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
        {
            await next();
            return;
        }
        AccountOperations accounts = http.RequestServices.GetRequiredService<AccountOperations>();
        string ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string path = (http.Request.Path.Value ?? string.Empty).TrimEnd('/').ToLowerInvariant();
        if (path == "/api/media/images")
        {
            await accounts.EnforceIpLimitAsync(ip, "media-upload-ip", 60, TimeSpan.FromMinutes(10), http.RequestAborted);
            await accounts.EnforceIpLimitAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ip, "media-upload-account", 20, TimeSpan.FromMinutes(10), http.RequestAborted);
            if (!http.User.IsInRole("Author") || !bool.TryParse(http.User.FindFirstValue("email_confirmed"), out bool confirmed) || !confirmed)
                throw new AccountOperationException(403, "media_permission_required", "A confirmed author is required.");
        }
        else if (path.StartsWith("/api/media/", StringComparison.Ordinal))
            await accounts.EnforceIpLimitAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ip, "media-mutation", 30, TimeSpan.FromMinutes(1), http.RequestAborted);
        else if (path.StartsWith("/api/editorial/", StringComparison.Ordinal))
        {
            bool draftSave = HttpMethods.IsPatch(http.Request.Method) && path.StartsWith("/api/editorial/articles/", StringComparison.Ordinal);
            await accounts.EnforceIpLimitAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ip, draftSave ? "draft-save" : "editorial-mutation", draftSave ? 120 : 30, TimeSpan.FromMinutes(1), http.RequestAborted);
        }
        if (path == "/api/installation/complete")
            await accounts.EnforceIpLimitAsync(ip, "installation", 10, TimeSpan.FromMinutes(10), http.RequestAborted);
        if (http.Request.Path.StartsWithSegments("/api/account/suggestions", StringComparison.OrdinalIgnoreCase)
            || http.Request.Path.StartsWithSegments("/api/staff/suggestions", StringComparison.OrdinalIgnoreCase))
            await accounts.EnforceIpLimitAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ip, "suggestion-mutation", 20, TimeSpan.FromMinutes(1), http.RequestAborted);
        if (path == "/api/auth/register")
            await accounts.EnforceIpLimitAsync(ip, "registration", 5, TimeSpan.FromHours(1), http.RequestAborted);
        if (path == "/api/auth/login")
            await accounts.EnforceIpLimitAsync(ip, "login", 15, TimeSpan.FromMinutes(1), http.RequestAborted);
        if (path is "/api/auth/register" or "/api/account/email-confirmation" or "/api/auth/forgot-password")
            await accounts.EnforceIpLimitAsync(ip, "email", 10, TimeSpan.FromHours(1), http.RequestAborted);
        try
        {
            await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            throw new AccountOperationException(400, "invalid_csrf", "A valid CSRF token is required.");
        }
        if (path != "/api/media/images")
        {
            await next();
            return;
        }
        // Acquire before model binding so multipart buffering cannot defeat the processor's memory bound.
        if (!await _mediaUpload.WaitAsync(0, http.RequestAborted))
            throw new AccountOperationException(429, "media_processing_busy", "Image processing is busy. Retry shortly.", retryAfterSeconds: 2);
        try
        { await next(); }
        finally { _mediaUpload.Release(); }
    }
}
