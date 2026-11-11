using Microsoft.AspNetCore.Identity;

namespace CityPortal.Identity;

internal sealed class Account : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
