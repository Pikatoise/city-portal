namespace CityPortal.Api.Contracts;

/// <summary>Changes a password using the current password and account version.</summary>
public sealed record ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }

    public string? NewPassword { get; init; }

    public long Version { get; init; }
}
