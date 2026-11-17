namespace CityPortal.Media;

/// <summary>Authorized image status without storage credentials or object keys.</summary>
public sealed record ImageResponse(Guid Id, string Name, string State, int Width, int Height, long Version, DateTimeOffset CreatedAtUtc, string? ErrorCode);
