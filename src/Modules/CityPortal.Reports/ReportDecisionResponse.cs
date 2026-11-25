namespace CityPortal.Reports;

public sealed record ReportDecisionResponse(Guid Id, Guid ActorId, string Outcome, string? Reason, DateTimeOffset DecidedAtUtc);
