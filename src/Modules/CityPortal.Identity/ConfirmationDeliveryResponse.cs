using System.Text.Json.Serialization;

namespace CityPortal.Identity;

public sealed class ConfirmationDeliveryResponse
{
    [JsonRequired]
    public string State { get; init; } = "Loading";

    [JsonRequired]
    public int ResendAfterSeconds { get; init; }
}
