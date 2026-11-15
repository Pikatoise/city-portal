namespace CityPortal.Media;

/// <summary>An image proposal or authorized shared-library entry.</summary>
public sealed record TemplateResponse(Guid Id, Guid ImageId, string Name, string Approval, bool Submitted, bool Archived, bool Blocked, string? Reason, long Version, bool Own);
