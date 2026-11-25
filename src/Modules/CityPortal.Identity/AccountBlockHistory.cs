namespace CityPortal.Identity;

/// <summary>A page of account block decisions, newest first.</summary>
public sealed record AccountBlockHistory(AccountBlockHistoryItem[] Items, bool HasNextPage);
