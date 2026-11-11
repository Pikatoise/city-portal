using CityPortal.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CityPortal.Api;

internal sealed class AccountRequestFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
        {
            await next();
            return;
        }
        AccountOperations accounts = http.RequestServices.GetRequiredService<AccountOperations>();
        string ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string path = (http.Request.Path.Value ?? string.Empty).TrimEnd('/').ToLowerInvariant();
        if (path == "/api/auth/register")
            await accounts.EnforceIpLimitAsync(ip, "registration", 5, TimeSpan.FromHours(1), http.RequestAborted);
        if (path == "/api/auth/login")
            await accounts.EnforceIpLimitAsync(ip, "login", 15, TimeSpan.FromMinutes(1), http.RequestAborted);
        if (path is "/api/auth/register" or "/api/account/email-confirmation")
            await accounts.EnforceIpLimitAsync(ip, "email", 10, TimeSpan.FromHours(1), http.RequestAborted);
        try
        {
            await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            throw new AccountOperationException(400, "invalid_csrf", "A valid CSRF token is required.");
        }
        await next();
    }
}
