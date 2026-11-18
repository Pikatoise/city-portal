namespace CityPortal.CitizenSubmissions;

internal sealed class Suggestion
{
    public Guid Id { get; init; }

    public Guid ResidentId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? FirstClaimedAtUtc { get; set; }

    public int Version { get; set; } = 1;

    public required string Title { get; set; }

    public required string Description { get; set; }

    public DateOnly? EventDate { get; set; }

    public string? Location { get; set; }

    public string? Contact { get; set; }
}
