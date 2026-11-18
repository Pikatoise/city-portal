namespace CityPortal.CitizenSubmissions;

/// <summary>A page of suggestions visible in the selected staff view.</summary>
public sealed record StaffSuggestionPage(StaffSuggestionResponse[] Items, bool HasMore);
