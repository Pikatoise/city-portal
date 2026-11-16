using System.Net;
using System.Net.Http.Json;
using CityPortal.Identity;
using CityPortal.News;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Fact]
    public async Task Discovery_RussianSearchFiltersAndReplacement_UseOnlyCurrentPublicationAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "City");
            TaxonomyResponse tag = await TaxonomyAsync(editor, "tags", "Walks");
            ArticleResponse article = await PublishDiscoveryAsync(owner, editor, Complete(category.Id, "Городские библиотеки") with { Summary = "Выставки картин", TagIds = [tag.Id] });
            ArticleResponse body = await PublishDiscoveryAsync(owner, editor, NewsDocumentTests.Content("На прогулках жители обсуждают набережную") with { Title = "Маршрут", Summary = "Планы", CategoryId = category.Id });
            await SaveAsync(owner, await CreateAsync(owner), Complete(category.Id, "Секретные библиотеки"));
            foreach (string word in new[] { "библиотека", "картина" })
                Assert.Equal(article.Id, Assert.Single((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString(word))).Items).Id);
            Assert.Equal(body.Id, Assert.Single((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("прогулка"))).Items).Id);
            Assert.Equal(article.Id, Assert.Single((await DiscoveryAsync(anonymous, $"?categoryId={category.Id}&tagId={tag.Id}&query=" + Uri.EscapeDataString("библиотека"))).Items).Id);
            Assert.Empty((await DiscoveryAsync(anonymous, "?categoryId=" + Guid.NewGuid())).Items);
            Assert.Empty((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("секретный"))).Items);
            AccountResponse profile = (await owner.GetFromJsonAsync<AccountResponse>("/api/account"))!;
            Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync("/api/account/display-name", new { profile.Version, displayName = "CurrentCredit" })).StatusCode);
            Assert.All((await DiscoveryAsync(anonymous)).Items, item => Assert.Equal("CurrentCredit", item.AuthorDisplayName));
            article = await ActAsync(owner, article, "working-version");
            article = await SaveAsync(owner, article, Complete(category.Id, "Неопубликованный черновик"));
            Assert.Empty((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("неопубликованный"))).Items);
            Assert.Equal(article.Id, Assert.Single((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("библиотека"))).Items).Id);
            article = await CorrectAsync(editor, article, Complete(category.Id, "Новые музеи"), "Verified change");
            Assert.Empty((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("библиотека"))).Items);
            Assert.Equal(article.Id, Assert.Single((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("музей"))).Items).Id);
            string json = await anonymous.GetStringAsync("/api/news/articles");
            foreach (string secret in new[] { "contentJson", "revisionId", "body", "Неопубликованный", "Секретные", "reason", "email" })
                Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
            article = await ActAsync(editor, article, "withdraw", "Hidden reason");
            Assert.DoesNotContain((await DiscoveryAsync(anonymous)).Items, item => item.Id == article.Id);
            await factory.Services.MigrateNewsAsync(CancellationToken.None);
            Assert.Equal(body.Id, Assert.Single((await DiscoveryAsync(anonymous)).Items).Id);
        }
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=0")]
    [InlineData("?from=2026-11-17&to=2026-11-16")]
    [InlineData("?to=9999-12-31")]
    [InlineData("?from=invalid")]
    [InlineData("?tagId=invalid")]
    public async Task Discovery_InvalidQueriesReturnValidationAsync(string query)
    {
        (ApiFactory factory, _, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient anonymous = Client(factory);
        using HttpResponseMessage response = await anonymous.GetAsync("/api/news/articles" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(400, (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Discovery_PagesDatesAndTiesAreBoundedAndDeterministicAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Dates");
            ArticleResponse article = await PublishedAsync(owner, editor, category.Id, "Библиотеки города");
            await ExecuteAsync(database, "UPDATE identity.\"SiteConfiguration\" SET \"TimeZone\"='Asia/Yekaterinburg'; UPDATE news.\"Articles\" SET \"FirstPublishedAtUtc\"='2026-11-15T19:00:00Z' WHERE \"Id\"=@id", article.Id);
            Assert.Single((await DiscoveryAsync(anonymous, "?from=2026-11-16&to=2026-11-16")).Items);
            await ExecuteAsync(database, "UPDATE news.\"Articles\" SET \"FirstPublishedAtUtc\"='2026-11-16T19:00:00Z' WHERE \"Id\"=@id", article.Id);
            Assert.Empty((await DiscoveryAsync(anonymous, "?from=2026-11-16&to=2026-11-16")).Items);
            await ExecuteAsync(database, "UPDATE identity.\"SiteConfiguration\" SET \"TimeZone\"='Europe/Berlin'; UPDATE news.\"Articles\" SET \"FirstPublishedAtUtc\"='2026-03-29T21:59:59Z' WHERE \"Id\"=@id", article.Id);
            Assert.Single((await DiscoveryAsync(anonymous, "?from=2026-03-29&to=2026-03-29")).Items);
            await ExecuteAsync(database, "UPDATE news.\"Articles\" SET \"FirstPublishedAtUtc\"='2026-03-29T22:00:00Z' WHERE \"Id\"=@id", article.Id);
            Assert.Empty((await DiscoveryAsync(anonymous, "?from=2026-03-29&to=2026-03-29")).Items);
            // Clone an already valid publication as pagination fixture data without defeating write throttles.
            await ExecuteAsync(database, """
                WITH copies AS (SELECT gen_random_uuid() AS id, gen_random_uuid() AS revision FROM generate_series(1,21)),
                added AS (
                  INSERT INTO news."Articles" ("Id","AuthorId","Version","PublicationState","WorkingRevisionId","PublishedRevisionId","LastSubmittedRevisionId","FirstPublishedAtUtc","CreatedAtUtc","UpdatedAtUtc","Featured")
                  SELECT c.id,a."AuthorId",1,'Published',c.revision,c.revision,c.revision,a."FirstPublishedAtUtc",a."CreatedAtUtc",a."UpdatedAtUtc",false
                  FROM copies c CROSS JOIN news."Articles" a WHERE a."Id"=@id RETURNING "Id")
                INSERT INTO news."Revisions" ("Id","ArticleId","State","ContentJson","Title","PlainText","PreviewHtml","Retained","CreatedAtUtc")
                SELECT c.revision,c.id,'Published',r."ContentJson",r."Title",r."PlainText",r."PreviewHtml",true,r."CreatedAtUtc"
                FROM copies c JOIN added a ON a."Id"=c.id CROSS JOIN news."Revisions" r JOIN news."Articles" source ON source."PublishedRevisionId"=r."Id" WHERE source."Id"=@id
                """, article.Id);
            await ExecuteAsync(database, "UPDATE news.\"Articles\" SET \"FirstPublishedAtUtc\"='2026-11-16T00:00:00Z' WHERE \"DeletedAtUtc\" IS NULL AND @id IS NOT NULL", article.Id);
            NewsPage<PublicArticleSummaryResponse> first = await DiscoveryAsync(anonymous);
            NewsPage<PublicArticleSummaryResponse> second = await DiscoveryAsync(anonymous, "?page=2");
            Assert.Equal(20, first.Items.Length);
            Assert.Equal(22, first.Total);
            Assert.Equal(2, second.Items.Length);
            Assert.Equal(22, first.Items.Concat(second.Items).Select(item => item.Id).Distinct().Count());
            Assert.Equal(first.Items.Select(item => item.Id), (await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("библиотека"))).Items.Select(item => item.Id));
            Assert.Empty((await DiscoveryAsync(anonymous, "?page=3")).Items);
            Assert.Equal(22, (await DiscoveryAsync(anonymous, "?pageSize=100")).Items.Length);
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/news/articles?query=" + new string('x', 201))).StatusCode);
        }
    }

    [Fact]
    public async Task Discovery_RelatedAndTaxonomyUsePublicTagThenCategoryOrderAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Visible");
            TaxonomyResponse other = await TaxonomyAsync(editor, "categories", "Other");
            await TaxonomyAsync(editor, "categories", "Private unused");
            TaxonomyResponse tag = await TaxonomyAsync(editor, "tags", "Shared");
            ArticleResponse source = await PublishDiscoveryAsync(owner, editor, Complete(category.Id, "Source") with { TagIds = [tag.Id] });
            ArticleResponse shared = await PublishDiscoveryAsync(owner, editor, Complete(other.Id, "Tagged older") with { TagIds = [tag.Id] });
            var sameCategory = new ArticleResponse[6];
            for (int i = 0; i < sameCategory.Length; i++)
                sameCategory[i] = await PublishedAsync(owner, editor, category.Id, "Category " + i);
            PublicArticleSummaryResponse[] related = (await anonymous.GetFromJsonAsync<PublicArticleSummaryResponse[]>($"/api/news/articles/{source.Id}/related"))!;
            Assert.Equal(5, related.Length);
            Assert.Equal(shared.Id, related[0].Id);
            Assert.DoesNotContain(related, item => item.Id == source.Id);
            Assert.Equal(5, related.Select(item => item.Id).Distinct().Count());
            Assert.Equal(sameCategory.Reverse().Take(4).Select(item => item.Id), related.Skip(1).Select(item => item.Id));
            NewsPage<PublicTaxonomyResponse> categories = (await anonymous.GetFromJsonAsync<NewsPage<PublicTaxonomyResponse>>("/api/news/categories"))!;
            Assert.Equal(2, categories.Total);
            Assert.DoesNotContain(categories.Items, item => item.Name == "Private unused");
            shared = await ActAsync(editor, shared, "withdraw", "Remove tag match");
            Assert.DoesNotContain((await anonymous.GetFromJsonAsync<PublicArticleSummaryResponse[]>($"/api/news/articles/{source.Id}/related"))!, item => item.Id == shared.Id);
            source = await ActAsync(editor, source, "withdraw", "Remove source");
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/news/articles/{source.Id}/related")).StatusCode);
            Assert.Empty((await anonymous.GetFromJsonAsync<NewsPage<PublicTaxonomyResponse>>("/api/news/tags"))!.Items);
        }
    }

    private static async Task<ArticleResponse> PublishDiscoveryAsync(HttpClient owner, HttpClient editor, ArticleContent content)
    {
        ArticleResponse article = await SaveAsync(owner, await CreateAsync(owner), content);
        article = await ActAsync(owner, article, "submit");
        article = await ActAsync(editor, article, "claim");
        article = await ActAsync(editor, article, "approve");
        return await ActAsync(owner, article, "publish");
    }

    private static async Task<NewsPage<PublicArticleSummaryResponse>> DiscoveryAsync(HttpClient anonymous, string query = "")
    {
        using HttpResponseMessage response = await anonymous.GetAsync("/api/news/articles" + query);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<NewsPage<PublicArticleSummaryResponse>>())!;
    }
}
