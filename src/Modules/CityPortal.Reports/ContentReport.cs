namespace CityPortal.Reports;

internal sealed class ContentReport
{
    public long Sequence { get; init; }

    public required string Kind { get; init; }

    public required Guid TargetId { get; init; }

    public required string Category { get; init; }

    public string? Text { get; init; }

    public required DateTimeOffset ReceivedAtUtc { get; init; }

    public Guid? DecisionId { get; set; }
}
