using System.Text.Json.Serialization;

namespace CityPortal.Identity;

public sealed class AccountResponse
{
    [JsonRequired]
    public Guid Id { get; init; }

    [JsonRequired]
    public string Email { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public bool EmailConfirmed { get; init; }

    [JsonRequired]
    public string[] Roles { get; init; } = [];

    [JsonRequired]
    public long Version { get; init; }

    [JsonRequired]
    public ConfirmationDeliveryResponse ConfirmationDelivery { get; init; } = new();
}
