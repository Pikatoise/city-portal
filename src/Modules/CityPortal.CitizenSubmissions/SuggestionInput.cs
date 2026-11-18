using System.ComponentModel.DataAnnotations;

namespace CityPortal.CitizenSubmissions;

/// <summary>A resident's text, event details, and optional contact information.</summary>
public sealed record SuggestionInput : IValidatableObject
{
    [Required, StringLength(200)]
    public required string Title { get; init; }

    [Required, StringLength(10000)]
    public required string Description { get; init; }

    public DateOnly? EventDate { get; init; }

    [StringLength(300)]
    public string? Location { get; init; }

    [StringLength(300)]
    public string? Contact { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach ((string field, string? value) in new (string, string?)[]
            { (nameof(Title), Title), (nameof(Description), Description), (nameof(Location), Location), (nameof(Contact), Contact) })
            if (value?.Contains('\0') == true)
                yield return new ValidationResult("Null characters are not allowed.", [field]);
    }
}
