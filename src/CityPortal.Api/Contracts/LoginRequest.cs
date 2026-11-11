namespace CityPortal.Api.Contracts;

public sealed class LoginRequest
{
    public string? Email { get; init; }

    public string? Password { get; init; }

    public bool RememberMe { get; init; }
}
