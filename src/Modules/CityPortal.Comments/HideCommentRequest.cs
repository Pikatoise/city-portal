using System.Text.Json.Serialization;

namespace CityPortal.Comments;

/// <summary>The mandatory reason for hiding a public comment.</summary>
public sealed record HideCommentRequest([property: JsonRequired] string Reason);
