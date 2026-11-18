namespace CityPortal.CitizenSubmissions;

internal sealed class SuggestionDeletion
{
    public Guid SuggestionId { get; init; }

    public Guid ActorId { get; init; }

    public DateTimeOffset DeletedAtUtc { get; init; }

    public required string Reason { get; init; }
}
