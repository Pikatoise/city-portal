using System.ComponentModel.DataAnnotations;

namespace CityPortal.Reports;

public sealed record DecideContentReportsRequest
{
    [Required, MaxLength(10)]
    public required string Outcome { get; init; }

    public required long ThroughSequence { get; init; }

    public long? TargetVersion { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}
