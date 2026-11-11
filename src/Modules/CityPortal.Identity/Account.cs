using Microsoft.AspNetCore.Identity;

namespace CityPortal.Identity;

internal sealed class Account : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public long Version { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid? LatestConfirmationMessageId { get; set; }
}
