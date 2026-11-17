using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>Expected version for an existing Media resource.</summary>
public sealed record MediaVersionRequest
{
    [Range(1, long.MaxValue)]
    public long Version { get; init; }
}
