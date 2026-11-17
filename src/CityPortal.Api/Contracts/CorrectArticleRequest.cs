using System.ComponentModel.DataAnnotations;
using CityPortal.News;

namespace CityPortal.Api.Contracts;

/// <summary>Publish an editorial correction and retain any superseded author proposal.</summary>
public sealed record CorrectArticleRequest
{
    [Range(1, long.MaxValue)]
    public long Version { get; init; }
    [Required]
    public required ArticleContent Content { get; init; }
    [Required]
    public required string Reason { get; init; }
}
