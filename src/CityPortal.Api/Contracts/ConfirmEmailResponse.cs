using System.Text.Json.Serialization;

namespace CityPortal.Api.Contracts;

public sealed class ConfirmEmailResponse
{
    [JsonRequired]
    public string Status { get; init; } = string.Empty;
}
