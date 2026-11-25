using System.ComponentModel.DataAnnotations;

namespace CityPortal.Reports;

public sealed record SubmitContentReportRequest
{
    [Required, MaxLength(40)]
    public required string Category { get; init; }

    [MaxLength(500)]
    public string? Text { get; init; }
}
