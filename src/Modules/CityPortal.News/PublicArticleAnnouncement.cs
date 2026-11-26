namespace CityPortal.News;

/// <summary>The title, summary and address of the current public article revision.</summary>
public sealed record PublicArticleAnnouncement(Guid RevisionId, string Title, string Summary, string PublicAddress);
