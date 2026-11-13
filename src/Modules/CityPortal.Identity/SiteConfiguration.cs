namespace CityPortal.Identity;

internal sealed class SiteConfiguration
{
    public int Id { get; set; } = 1;

    public string? Name { get; set; }

    public string? City { get; set; }

    public string? Region { get; set; }

    public string? TimeZone { get; set; }
}
