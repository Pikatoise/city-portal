namespace CityPortal.Identity;

internal sealed class AccountSession
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool Revoked { get; set; }
}
