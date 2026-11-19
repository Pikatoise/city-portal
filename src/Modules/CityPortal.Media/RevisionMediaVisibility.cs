namespace CityPortal.Media;

/// <summary>The audience of existing Media references for a News revision.</summary>
public sealed record RevisionMediaVisibility(Guid RevisionId, Guid? ReviewerId, bool Submitted, bool IsPublic, bool IsCurrent, bool AuthorVisible);
