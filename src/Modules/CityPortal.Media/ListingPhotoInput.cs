using System.ComponentModel.DataAnnotations;

namespace CityPortal.Media;

/// <summary>A staged photo and its alternative text, in the desired display order.</summary>
public sealed record ListingPhotoInput
{
    public required Guid ImageId { get; init; }

    [Required, MaxLength(300)]
    public required string AlternativeText { get; init; }
}
