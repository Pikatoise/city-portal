namespace CityPortal.Identity;

internal sealed class RoleChange
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public string ActorKind { get; set; } = "Account";

    public Guid TargetAccountId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string[] OldRoles { get; set; } = [];

    public string[] NewRoles { get; set; } = [];
}
