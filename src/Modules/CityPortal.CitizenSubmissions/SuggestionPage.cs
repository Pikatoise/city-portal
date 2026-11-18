namespace CityPortal.CitizenSubmissions;

/// <summary>A page of the resident's current suggestions.</summary>
public sealed record SuggestionPage(SuggestionResponse[] Items, bool HasMore);
