namespace CityPortal.Identity;

internal sealed class RequestAttempt
{
    public long Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
