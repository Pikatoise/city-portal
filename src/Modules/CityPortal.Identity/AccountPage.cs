using System.Text.Json.Serialization;

namespace CityPortal.Identity;

public sealed record AccountPage(
    [property: JsonRequired] AccountSummary[] Items,
    [property: JsonRequired] int TotalCount,
    [property: JsonRequired] int Page,
    [property: JsonRequired] int PageSize);
