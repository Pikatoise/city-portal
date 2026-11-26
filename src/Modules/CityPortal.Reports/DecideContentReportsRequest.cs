using System.ComponentModel.DataAnnotations;

namespace CityPortal.Reports;

public sealed record DecideContentReportsRequest
{
    private readonly string? _reason;

    [Required, MaxLength(10)]
    public required string Outcome { get; init; }

    public required long ThroughSequence { get; init; }

    public long? TargetVersion { get; init; }

    [MaxLength(500)]
    public string? Reason
    {
        get => _reason;
        init => _reason = value?.Trim();
    }
}
