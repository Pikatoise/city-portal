namespace CityPortal.Notifications;

public sealed class AccountMailOptions
{
    public string PublicOrigin { get; set; } = "https://localhost:8443";

    public string PortalName { get; set; } = "CityPortal";

    public string SmtpHost { get; set; } = "localhost";

    public int SmtpPort { get; set; } = 1025;

    public string SenderAddress { get; set; } = "noreply@cityportal.test";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool UseTls { get; set; } = true;

    public bool ImplicitTls { get; set; }
}
