using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using CityPortal.Identity;
using CityPortal.Integrations;
using CityPortal.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RssController : ControllerBase
{
    private readonly PublicIntegrationReads _integrations;
    private readonly PublicNewsDiscoveryOperations _news;
    private readonly SiteInstallation _installation;
    private readonly IConfiguration _configuration;

    public RssController(PublicIntegrationReads integrations, PublicNewsDiscoveryOperations news, SiteInstallation installation, IConfiguration configuration)
    {
        _integrations = integrations;
        _news = news;
        _installation = installation;
        _configuration = configuration;
    }

    /// <summary>Returns the latest twenty currently published articles as RSS 2.0.</summary>
    [HttpGet("/rss.xml", Name = "getNewsRss")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, "application/rss+xml")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (!await _integrations.IsRssEnabledAsync(cancellationToken))
            return NotFound();
        InstallationInfo installation = await _installation.GetAsync(cancellationToken);
        if (!installation.Installed)
            return NotFound();
        Uri? origin = IntegrationOrigin.FromConfiguration(_configuration);
        if (origin is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "RSS is unavailable");
        NewsPage<PublicArticleSummaryResponse> articles = await _news.ListAsync(new PublicNewsQuery(null, null, null, null, null, 1, 20), cancellationToken);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var document = new XElement("rss", new XAttribute("version", "2.0"), new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XElement("channel",
                new XElement("title", XmlText(installation.Name ?? "CityPortal")),
                new XElement("link", new Uri(origin, "/news").AbsoluteUri),
                new XElement("description", "Latest city news"),
                new XElement("language", "ru"),
                new XElement(atom + "link", new XAttribute("href", new Uri(origin, "/rss.xml").AbsoluteUri),
                    new XAttribute("rel", "self"), new XAttribute("type", "application/rss+xml")),
                articles.Items.Select(article => new XElement("item",
                    new XElement("title", XmlText(article.Title)),
                    new XElement("description", WebUtility.HtmlEncode(XmlText(article.Summary))),
                    new XElement("link", new Uri(origin, article.CanonicalUrl).AbsoluteUri),
                    new XElement("guid", new XAttribute("isPermaLink", "false"), $"urn:uuid:{article.Id:D}"),
                    new XElement("pubDate", article.FirstPublishedAtUtc.ToString("R", CultureInfo.InvariantCulture))))));
        return Content(document.ToString(SaveOptions.DisableFormatting), "application/rss+xml", Encoding.UTF8);
    }

    private static string XmlText(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (rune.Value is 0x9 or 0xA or 0xD or (>= 0x20 and <= 0xD7FF) or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF))
                text.Append(rune.ToString());
        }
        return text.ToString();
    }
}
