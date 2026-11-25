using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>Blocks the specified version of an account with a mandatory reason.</summary>
public sealed record BlockAccountRequest
{
    [Range(1, long.MaxValue)]
    public required long Version { get; init; }

    [Required]
    public required string Reason { get; init; }
}
