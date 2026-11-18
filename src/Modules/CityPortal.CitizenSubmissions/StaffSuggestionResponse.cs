namespace CityPortal.CitizenSubmissions;

/// <summary>A suggestion visible to an author or moderator under the current assignment rules.</summary>
public sealed record StaffSuggestionResponse(Guid Id, string Title, string Description, DateOnly? EventDate,
    string? Location, string? Contact, bool Assigned, DateTimeOffset? ExpiresAtUtc, int Version,
    Guid? ArticleId, bool CanReturn, bool Published, bool CanImportImages);
