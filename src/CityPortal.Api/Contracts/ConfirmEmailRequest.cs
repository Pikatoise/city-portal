namespace CityPortal.Api.Contracts;

public sealed class ConfirmEmailRequest
{
    public Guid LinkId { get; init; }

    public string? Token { get; init; }
}
