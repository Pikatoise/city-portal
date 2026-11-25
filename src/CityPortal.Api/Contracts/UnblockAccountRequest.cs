using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>Removes an account block using its current version.</summary>
public sealed record UnblockAccountRequest
{
    [Range(1, long.MaxValue)]
    public required long Version { get; init; }
}
