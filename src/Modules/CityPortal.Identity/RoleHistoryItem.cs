using System.Text.Json.Serialization;

namespace CityPortal.Identity;

public sealed record RoleHistoryItem(
    [property: JsonRequired] Guid Id,
    Guid? ActorId,
    [property: JsonRequired] string ActorKind,
    [property: JsonRequired] Guid TargetAccountId,
    [property: JsonRequired] DateTimeOffset CreatedAtUtc,
    [property: JsonRequired] string[] OldRoles,
    [property: JsonRequired] string[] NewRoles);
