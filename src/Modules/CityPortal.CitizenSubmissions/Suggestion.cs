namespace CityPortal.CitizenSubmissions;

internal sealed class Suggestion
{
    public Guid Id { get; init; }

    public Guid ResidentId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? FirstClaimedAtUtc { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public Guid? ArticleId { get; private set; }

    public DateTimeOffset? PausedAtUtc { get; private set; }

    public int Version { get; private set; } = 1;

    public required string Title { get; set; }

    public required string Description { get; set; }

    public DateOnly? EventDate { get; set; }

    public string? Location { get; set; }

    public string? Contact { get; set; }

    internal bool IsExpiredAt(DateTimeOffset now) => PausedAtUtc is null && ExpiresAtUtc <= now;

    internal void Claim(Guid authorId, Guid articleId, DateTimeOffset claimedAtUtc)
    {
        ArticleId = articleId;
        AssigneeId = authorId;
        FirstClaimedAtUtc ??= claimedAtUtc;
        PausedAtUtc = claimedAtUtc;
        Version++;
    }

    internal bool ReturnToQueue(DateTimeOffset draftDeletedAtUtc)
    {
        if (PausedAtUtc is not DateTimeOffset paused)
            return false;

        TimeSpan pause = draftDeletedAtUtc > paused ? draftDeletedAtUtc - paused : TimeSpan.Zero;
        ExpiresAtUtc = ExpiresAtUtc.Add(pause);
        AssigneeId = null;
        PausedAtUtc = null;
        ArticleId = null;
        Version++;
        return true;
    }
}
