using System.ComponentModel.DataAnnotations;

namespace CityPortal.Classifieds;

/// <summary>A decision on the submitted version with a reason of up to 500 characters after trimming.</summary>
public sealed record DecideListingReviewRequest
{
    [Range(1, int.MaxValue)]
    public required int Version { get; init; }

    [Required]
    public required string Reason { get; init; }
}
