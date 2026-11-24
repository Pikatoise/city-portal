using System.ComponentModel.DataAnnotations;

namespace CityPortal.Classifieds;

/// <summary>Updates a catalog entry using the last version read by the administrator.</summary>
public sealed record UpdateClassifiedsCatalogEntryRequest
{
    [Range(1, int.MaxValue)]
    public required int Version { get; init; }

    [Required]
    public required SaveClassifiedsCatalogEntryRequest Entry { get; init; }
}
