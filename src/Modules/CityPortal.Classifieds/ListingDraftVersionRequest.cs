using System.ComponentModel.DataAnnotations;

namespace CityPortal.Classifieds;

/// <summary>Changes the draft's review status using the last version read.</summary>
public sealed record ListingDraftVersionRequest
{
    [Range(1, int.MaxValue)]
    public required int Version { get; init; }
}
