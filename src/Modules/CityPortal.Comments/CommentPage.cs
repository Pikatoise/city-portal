using System.Text.Json.Serialization;

namespace CityPortal.Comments;

/// <summary>A bounded page of comments ordered newest first.</summary>
public sealed record CommentPage([property: JsonRequired] CommentResponse[] Items, string? NextCursor,
    [property: JsonRequired] bool CanModerate);
