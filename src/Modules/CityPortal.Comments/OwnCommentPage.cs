using System.Text.Json.Serialization;

namespace CityPortal.Comments;

/// <summary>A bounded page of the account's comments with its current deletion permission.</summary>
public sealed record OwnCommentPage([property: JsonRequired] OwnCommentResponse[] Items, [property: JsonRequired] bool HasMore,
    [property: JsonRequired] bool CanDelete);
