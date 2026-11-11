namespace CityPortal.Api.Contracts;

/// <summary>Public application metadata for the frontend connection check.</summary>
public sealed record SystemInfoResponse(string Name, int Stage);
