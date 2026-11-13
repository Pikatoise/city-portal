namespace CityPortal.Identity;

public sealed record InstallationInfo(bool Installed, string? Name, string? City, string? Region, string? TimeZone);
