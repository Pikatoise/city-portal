namespace CityPortal.Identity;

internal sealed class AccountBlockDecision
{
    public Guid Id { get; init; }

    public Guid TargetId { get; init; }

    public Guid ActorId { get; init; }

    public DateTimeOffset DecidedAtUtc { get; init; }

    public string? Reason { get; init; }
}
