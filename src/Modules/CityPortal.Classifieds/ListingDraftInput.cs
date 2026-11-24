using System.ComponentModel.DataAnnotations;
using CityPortal.Media;

namespace CityPortal.Classifieds;

/// <summary>Optional fields the owner can save before submitting the listing.</summary>
public sealed record ListingDraftInput : IValidatableObject
{
    [MaxLength(200)]
    public string? Title { get; init; }

    [MaxLength(10000)]
    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? DistrictId { get; init; }

    [Range(typeof(long), "0", "999999999999")]
    public long? Price { get; init; }

    [MaxLength(50)]
    public string? Phone { get; init; }

    [MaxLength(254)]
    public string? Email { get; init; }

    [MaxLength(5)]
    public ListingPhotoInput[]? Photos { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach ((string name, string? value) in new[] { (nameof(Title), Title), (nameof(Description), Description), (nameof(Phone), Phone), (nameof(Email), Email) })
            if (value?.Contains('\0') == true)
                yield return new ValidationResult("Remove unsupported characters.", [name]);
    }
}
