namespace CityPortal.Api.Contracts;

/// <summary>Identifies the public revision being announced.</summary>
public sealed record SuggestTelegramAnnouncementRequest(Guid RevisionId);
