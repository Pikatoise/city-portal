namespace CityPortal.Notifications;

/// <summary>A recipient resolved by the owning account module.</summary>
public sealed record WorkflowRecipient(Guid AccountId, string? Email);
