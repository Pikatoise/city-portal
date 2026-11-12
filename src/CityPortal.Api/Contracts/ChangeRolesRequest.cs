namespace CityPortal.Api.Contracts;

/// <summary>Desired staff roles and the last observed account version.</summary>
public sealed record ChangeRolesRequest
{
    public string[]? Roles { get; init; }

    public long Version { get; init; }
}
