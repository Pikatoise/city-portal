namespace CityPortal.Reports;

internal sealed class ReportDecision
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid ActorId { get; init; }

    public required string Outcome { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset DecidedAtUtc { get; init; }
}
