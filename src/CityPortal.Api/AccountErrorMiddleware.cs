using System.Diagnostics;
using CityPortal.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api;

internal sealed class AccountErrorMiddleware
{
    private readonly RequestDelegate _next;

    public AccountErrorMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AccountOperationException exception)
        {
            context.Response.StatusCode = exception.Status;
            context.Response.Headers.CacheControl = "no-store";
            if (exception.RetryAfterSeconds > 0)
                context.Response.Headers.RetryAfter = exception.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var details = new ProblemDetails { Status = exception.Status, Title = exception.Message };
            details.Extensions["code"] = exception.Code;
            details.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            if (exception.Errors is not null)
                details.Extensions["errors"] = exception.Errors;
            if (exception.RetryAfterSeconds > 0)
                details.Extensions["retryAfterSeconds"] = exception.RetryAfterSeconds;
            await context.Response.WriteAsJsonAsync(details, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
