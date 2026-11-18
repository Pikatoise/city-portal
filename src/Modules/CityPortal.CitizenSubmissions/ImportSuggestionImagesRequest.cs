using System.ComponentModel.DataAnnotations;

namespace CityPortal.CitizenSubmissions;

/// <summary>Images selected from the assigned suggestion for the current article draft.</summary>
public sealed record ImportSuggestionImagesRequest
{
    [Required, MinLength(1), MaxLength(5)]
    public required Guid[] ImageIds { get; init; }
}
