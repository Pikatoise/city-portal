using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

public sealed class InstallSiteRequest
{
    [Required]
    public string Email { get; init; } = "";

    [Required]
    public string DisplayName { get; init; } = "";

    [Required]
    public string Password { get; init; } = "";

    [Required, MaxLength(120)]
    public string Name { get; init; } = "";

    [Required, MaxLength(120)]
    public string City { get; init; } = "";

    [MaxLength(120)]
    public string Region { get; init; } = "";

    [Required, MaxLength(100)]
    public string TimeZone { get; init; } = "UTC";

    [Required, MaxLength(253)]
    public string SmtpHost { get; init; } = "";

    [Range(1, 65535)]
    public int SmtpPort { get; init; } = 587;

    [Required, EmailAddress, MaxLength(254)]
    public string SenderAddress { get; init; } = "";

    [MaxLength(1000)]
    public string? SmtpUsername { get; init; }

    [MaxLength(1000)]
    public string? SmtpPassword { get; init; }

    public bool UseTls { get; init; } = true;

    public bool ImplicitTls { get; init; }
}
