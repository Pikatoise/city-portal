using System.Text.Json.Serialization;

namespace CityPortal.Comments;

/// <summary>Plain-text comment body. The trimmed value contains 1 to 500 characters.</summary>
public sealed record CreateCommentRequest([property: JsonRequired] string Body);
