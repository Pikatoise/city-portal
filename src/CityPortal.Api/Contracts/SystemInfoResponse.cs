using System.Text.Json.Serialization;

namespace CityPortal.Api.Contracts;

/// <summary>Public application metadata for the frontend connection check.</summary>
public sealed record SystemInfoResponse
{
    public SystemInfoResponse(string name, int stage)
    {
        Name = name;
        Stage = stage;
    }

    public string Name { get; init; }

    public int Stage { get; init; }

    [JsonRequired]
    public string TimeZone { get; init; } = "UTC";

    [JsonRequired]
    public string City { get; init; } = "";

    [JsonRequired]
    public string Region { get; init; } = "";

    public void Deconstruct(out string name, out int stage)
    {
        name = Name;
        stage = Stage;
    }
}
