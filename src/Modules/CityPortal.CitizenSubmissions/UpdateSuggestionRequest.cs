using System.ComponentModel.DataAnnotations;

namespace CityPortal.CitizenSubmissions;

/// <summary>Replacement details and the version last read by the resident.</summary>
public sealed record UpdateSuggestionRequest
{
    [Required]
    public required SuggestionInput Details { get; init; }

    [Range(1, int.MaxValue)]
    public required int Version { get; init; }
}
