using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>Versioned template decision or author resubmission.</summary>
public sealed record TemplateActionRequest
{
    [Range(1, long.MaxValue)]
    public long Version { get; init; }
    public string? Reason { get; init; }
    public Guid? ImageId { get; init; }
    public string? Name { get; init; }
}
