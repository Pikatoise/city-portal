using System.ComponentModel.DataAnnotations;
using CityPortal.News;

namespace CityPortal.Api.Contracts;

/// <summary>Save an incomplete draft only if its expected record version is current.</summary>
public sealed record SaveArticleRequest
{
    [Range(1, long.MaxValue)]
    public long Version { get; init; }
    [Required]
    public required ArticleContent Content { get; init; }
}
