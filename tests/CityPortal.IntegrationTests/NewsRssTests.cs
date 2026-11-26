using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using CityPortal.Integrations;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Fact]
    public async Task Rss_UsesOnlyCurrentPublicContent_AndKeepsGuidAcrossCorrectionAndRepublicationAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync(new Dictionary<string, string> { ["PublicOrigin"] = "https://city.example" });
        await using ApiFactory lifetime = factory;
        (_, HttpClient author) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (author)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            using HttpResponseMessage disabled = await anonymous.GetAsync("/rss.xml");
            Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
            await EnableRssAsync(factory);
            Assert.Empty(await RssItemsAsync(anonymous));

            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Feed");
            ArticleResponse article = await PublishDiscoveryAsync(author, editor, Complete(category.Id, "Books & papers") with { Summary = "<aside>Public summary</aside>" });
            await SaveAsync(author, await CreateAsync(author), Complete(category.Id, "Private item"));
            string guid = $"urn:uuid:{article.Id:D}";
            XElement first = Assert.Single(await RssItemsAsync(anonymous));
            Assert.Equal("Books & papers", first.Element("title")!.Value);
            Assert.Equal("&lt;aside&gt;Public summary&lt;/aside&gt;", first.Element("description")!.Value);
            Assert.Equal("https://city.example" + article.PublicAddress, first.Element("link")!.Value);
            Assert.Equal(guid, first.Element("guid")!.Value);
            Assert.Equal("false", first.Element("guid")!.Attribute("isPermaLink")!.Value);
            string publicationDate = first.Element("pubDate")!.Value;

            article = await ActAsync(author, article, "working-version");
            article = await SaveAsync(author, article, Complete(category.Id, "Unpublished change"));
            Assert.Equal("Books & papers", Assert.Single(await RssItemsAsync(anonymous)).Element("title")!.Value);
            article = await CorrectAsync(editor, article, Complete(category.Id, "Corrected feed item"), "Verified change");
            XElement corrected = Assert.Single(await RssItemsAsync(anonymous));
            Assert.Equal("Corrected feed item", corrected.Element("title")!.Value);
            Assert.Equal("https://city.example" + article.PublicAddress, corrected.Element("link")!.Value);
            Assert.Equal(guid, corrected.Element("guid")!.Value);
            Assert.Equal(publicationDate, corrected.Element("pubDate")!.Value);

            article = await ActAsync(editor, article, "withdraw", "Hidden reason");
            Assert.Empty(await RssItemsAsync(anonymous));
            article = Assert.IsType<ArticleResponse>(await author.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"));
            article = await SaveAsync(author, article, Complete(category.Id, "Republished feed item"));
            article = await ActAsync(author, article, "submit", "Verified correction");
            article = await ActAsync(editor, article, "approve");
            article = await ActAsync(author, article, "publish");
            XElement republished = Assert.Single(await RssItemsAsync(anonymous));
            Assert.Equal("Republished feed item", republished.Element("title")!.Value);
            Assert.Equal("https://city.example" + article.PublicAddress, republished.Element("link")!.Value);
            Assert.Equal(guid, republished.Element("guid")!.Value);
            Assert.Equal(publicationDate, republished.Element("pubDate")!.Value);
        }
    }

    [Fact]
    public async Task Rss_ReturnsOnlyTheNewestTwentyEntriesInFirstPublicationOrderAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient author) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (author)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            await EnableRssAsync(factory);
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Feed order");
            ArticleResponse original = await PublishedAsync(author, editor, category.Id, "Oldest item");
            await ExecuteAsync(database, """
                WITH copies AS (
                    SELECT number, gen_random_uuid() AS id, gen_random_uuid() AS revision FROM generate_series(1,21) AS number
                ), added AS (
                    INSERT INTO news."Articles" ("Id","AuthorId","Version","PublicationState","WorkingRevisionId","PublishedRevisionId",
                        "LastSubmittedRevisionId","FirstPublishedAtUtc","CreatedAtUtc","UpdatedAtUtc","Featured","PublicAddress","FirstPublishedLocalDate")
                    SELECT c.id,a."AuthorId",1,'Published',c.revision,c.revision,c.revision,a."FirstPublishedAtUtc" + c.number * interval '1 minute',
                        a."CreatedAtUtc",a."UpdatedAtUtc",false,'/news/feed-fixture-' || c.number,a."FirstPublishedLocalDate"
                    FROM copies c CROSS JOIN news."Articles" a WHERE a."Id"=@id RETURNING "Id"
                )
                INSERT INTO news."Revisions" ("Id","ArticleId","State","ContentJson","Title","PlainText","PreviewHtml","Retained","CreatedAtUtc")
                SELECT c.revision,c.id,'Published',r."ContentJson",r."Title",r."PlainText",r."PreviewHtml",true,r."CreatedAtUtc"
                FROM copies c JOIN added a ON a."Id"=c.id CROSS JOIN news."Revisions" r
                JOIN news."Articles" source ON source."PublishedRevisionId"=r."Id" WHERE source."Id"=@id
                """, original.Id);
            XElement[] items = await RssItemsAsync(anonymous);
            Assert.Equal(Enumerable.Range(2, 20).Reverse().Select(number => $"/news/feed-fixture-{number}"),
                items.Select(item => new Uri(item.Element("link")!.Value).AbsolutePath));
        }
    }

    private static async Task EnableRssAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IntegrationDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        IntegrationSettings settings = await context.Settings.SingleAsync();
        settings.RssEnabled = true;
        await context.SaveChangesAsync();
    }

    private static async Task<XElement[]> RssItemsAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/rss.xml");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/rss+xml", response.Content.Headers.ContentType?.MediaType);
        var feed = XElement.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2.0", feed.Attribute("version")!.Value);
        return feed.Element("channel")!.Elements("item").ToArray();
    }
}
