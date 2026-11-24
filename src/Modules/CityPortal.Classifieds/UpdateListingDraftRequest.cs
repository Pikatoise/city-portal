using System.ComponentModel.DataAnnotations;

namespace CityPortal.Classifieds;

/// <summary>Replaces an owner's draft fields using the last version read.</summary>
public sealed record UpdateListingDraftRequest
{
    [Range(1, int.MaxValue)]
    public required int Version { get; init; }

    [Required]
    public required ListingDraftInput Details { get; init; }
}
