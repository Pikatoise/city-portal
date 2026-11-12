namespace CityPortal.Api.Contracts;

/// <summary>Updates the current account name at the expected version.</summary>
public sealed record ChangeDisplayNameRequest
{
    public string? DisplayName { get; init; }

    public long Version { get; init; }
}
