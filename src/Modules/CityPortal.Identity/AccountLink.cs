namespace CityPortal.Identity;

internal sealed class AccountLink
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public string State { get; set; } = "Active";
}
