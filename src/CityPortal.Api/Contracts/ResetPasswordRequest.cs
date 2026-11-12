namespace CityPortal.Api.Contracts;

/// <summary>Consumes a one-time recovery link and sets a new password.</summary>
public sealed record ResetPasswordRequest
{
    public Guid LinkId { get; init; }

    public string? Token { get; init; }

    public string? NewPassword { get; init; }
}
