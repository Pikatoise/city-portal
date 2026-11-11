using System.Text.Json.Serialization;

namespace CityPortal.Api.Contracts;

public sealed class CsrfResponse
{
    [JsonRequired]
    public string Token { get; init; } = string.Empty;
}
