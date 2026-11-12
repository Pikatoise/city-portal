using System.Text.Json.Serialization;

namespace CityPortal.Notifications;

public sealed record EmailDeliveryPage(
    [property: JsonRequired] EmailDeliverySummary[] Items,
    [property: JsonRequired] int TotalCount,
    [property: JsonRequired] int Page,
    [property: JsonRequired] int PageSize);
