namespace CityPortal.Media;

public sealed record TemplateHistoryResponse(Guid Id, Guid ImageId, Guid ActorId, string Action, string? Reason, DateTimeOffset CreatedAtUtc, long Version, string Approval, bool Archived, bool Blocked, string Name);
