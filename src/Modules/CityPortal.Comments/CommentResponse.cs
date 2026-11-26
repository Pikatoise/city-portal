using System.Text.Json.Serialization;

namespace CityPortal.Comments;

/// <summary>A comment and the current viewer's deletion permission.</summary>
public sealed record CommentResponse([property: JsonRequired] Guid Id, [property: JsonRequired] string DisplayName,
    [property: JsonRequired] string Body, [property: JsonRequired] DateTimeOffset CreatedAtUtc, [property: JsonRequired] bool CanDelete);
