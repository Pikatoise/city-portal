using System.Text.Json.Serialization;

namespace CityPortal.Comments;

/// <summary>An undeleted comment and the article URL when the article is public.</summary>
public sealed record OwnCommentResponse([property: JsonRequired] Guid Id, [property: JsonRequired] string Body,
    [property: JsonRequired] DateTimeOffset CreatedAtUtc, [property: JsonRequired] string? ArticleUrl);
