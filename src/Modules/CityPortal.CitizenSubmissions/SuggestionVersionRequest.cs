using System.ComponentModel.DataAnnotations;

namespace CityPortal.CitizenSubmissions;

/// <summary>The last version retrieved by the author.</summary>
public sealed record SuggestionVersionRequest
{
    [Range(1, int.MaxValue)]
    public required int Version { get; init; }
}
