using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>A name and archival update; existing entries require the expected version.</summary>
public sealed record SaveTaxonomyRequest
{
    public long Version { get; init; }
    [Required]
    public string Name { get; init; } = "";
    public bool Archived { get; init; }
}
