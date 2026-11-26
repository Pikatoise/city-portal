using Microsoft.Extensions.Configuration;

namespace CityPortal.Integrations;

public static class IntegrationOrigin
{
    public static Uri? FromConfiguration(IConfiguration configuration) =>
        ParseHttps(configuration["PublicOrigin"] ?? configuration["Mail:PublicOrigin"] ?? "https://localhost:8443");

    public static Uri? ParseHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? origin) && origin.Scheme == Uri.UriSchemeHttps
            && origin.UserInfo.Length == 0 && origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0
            ? origin : null;
}
