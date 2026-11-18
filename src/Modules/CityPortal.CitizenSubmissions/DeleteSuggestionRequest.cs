using System.ComponentModel.DataAnnotations;

namespace CityPortal.CitizenSubmissions;

/// <summary>The version to delete and the reason to retain in the audit record.</summary>
public sealed record DeleteSuggestionRequest
{
    [Range(1, int.MaxValue)]
    public required int Version { get; init; }

    [Required, StringLength(1000)]
    public required string Reason { get; init; }
}
