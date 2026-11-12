using System.Text.Json.Serialization;

namespace CityPortal.Identity;

public sealed record RoleHistoryPage(
    [property: JsonRequired] RoleHistoryItem[] Items,
    [property: JsonRequired] int TotalCount,
    [property: JsonRequired] int Page,
    [property: JsonRequired] int PageSize);
