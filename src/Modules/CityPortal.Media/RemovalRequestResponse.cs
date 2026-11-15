namespace CityPortal.Media;

public sealed record RemovalRequestResponse(Guid Id, Guid TemplateId, Guid OwnerId, DateTimeOffset CreatedAtUtc, DateTimeOffset? ResolvedAtUtc, long Version);
