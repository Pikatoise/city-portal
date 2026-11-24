using System.ComponentModel.DataAnnotations;

namespace CityPortal.Classifieds;

/// <summary>The display name and settings for a category or district.</summary>
public sealed record SaveClassifiedsCatalogEntryRequest
{
    [Required, MaxLength(120)]
    public required string Name { get; init; }

    public bool RequiresPrice { get; init; }

    public bool Archived { get; init; }
}
