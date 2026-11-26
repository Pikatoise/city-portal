namespace CityPortal.Reports;

internal sealed class ReportedContent
{
    public required string Kind { get; init; }

    public required Guid Id { get; init; }

    public Guid? OwnerId { get; init; }

    public required string Title { get; set; }

    public string? SnapshotText { get; set; }

    public Guid? DirectDecisionId { get; set; }

    public List<ContentReport> Reports { get; } = [];
}
