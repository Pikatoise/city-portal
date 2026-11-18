namespace CityPortal.CitizenSubmissions;

internal sealed class Suggestion
{
    public Guid Id { get; init; }

    public Guid ResidentId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? FirstClaimedAtUtc { get; set; }

    public Guid? AssigneeId { get; set; }

    public Guid? ArticleId { get; set; }

    public DateTimeOffset? PausedAtUtc { get; set; }

    public int Version { get; set; } = 1;

    public required string Title { get; set; }

    public required string Description { get; set; }

    public DateOnly? EventDate { get; set; }

    public string? Location { get; set; }

    public string? Contact { get; set; }
}
