namespace CityPortal.Api.Contracts;

/// <summary>Requests a recovery Email without disclosing account existence.</summary>
public sealed record ForgotPasswordRequest
{
    public string? Email { get; init; }
}
