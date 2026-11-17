using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>A versioned editorial action with optional workflow feedback.</summary>
public sealed record ReviewArticleRequest
{
    [Range(1, long.MaxValue)]
    public long Version { get; init; }
    public string? Reason { get; init; }
}
