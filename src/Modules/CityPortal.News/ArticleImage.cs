namespace CityPortal.News;

/// <summary>An image placement with contextual alternative text and an optional caption.</summary>
public sealed record ArticleImage(Guid MediaId, string Alt = "", string Caption = "");
