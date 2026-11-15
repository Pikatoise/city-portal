using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>Proposes an owned Ready image for the shared library.</summary>
public sealed record CreateTemplateRequest
{
    public Guid ImageId { get; init; }
    [Required]
    public string Name { get; init; } = "";
}
