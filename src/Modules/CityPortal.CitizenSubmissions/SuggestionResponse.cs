namespace CityPortal.CitizenSubmissions;

/// <summary>A private view of a resident's suggestion.</summary>
public sealed record SuggestionResponse(Guid Id, string Title, string Description, DateOnly? EventDate,
    string? Location, string? Contact, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc, bool CanEdit, int Version,
    string Status, string? PublicArticleUrl);
