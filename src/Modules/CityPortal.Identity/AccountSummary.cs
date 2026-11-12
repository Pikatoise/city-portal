using System.Text.Json.Serialization;

namespace CityPortal.Identity;

/// <summary>Account metadata for administration; contains no credential or link secrets.</summary>
public sealed record AccountSummary(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Email,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] bool EmailConfirmed,
    [property: JsonRequired] string[] Roles,
    [property: JsonRequired] long Version);
