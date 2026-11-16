using System.Net;
using System.Net.Http.Json;
using CityPortal.News;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Fact]
    public async Task Curation_CategoriesPersistOrderVersionPermissionsAndAuditAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient editor) = await ActorAsync(factory, database, "Administrator");
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using (editor)
        using (owner)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse first = await TaxonomyAsync(editor, "categories", "Culture");
            TaxonomyResponse second = await TaxonomyAsync(editor, "categories", "Transport");
            HomepageSelectionResponse selection = (await editor.GetFromJsonAsync<HomepageSelectionResponse>("/api/editorial/homepage"))!;
            var input = new { selection.Version, firstCategoryId = first.Id, secondCategoryId = second.Id };
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync("/api/editorial/homepage", input)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/editorial/homepage", input)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync("/api/editorial/homepage", new { selection.Version, firstCategoryId = first.Id, secondCategoryId = first.Id })).StatusCode);
            using HttpResponseMessage saved = await editor.PutAsJsonAsync("/api/editorial/homepage", input);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            HomepageSelectionResponse changed = (await saved.Content.ReadFromJsonAsync<HomepageSelectionResponse>())!;
            Assert.Equal(selection.Version + 1, changed.Version);
            Assert.Equal(HttpStatusCode.Conflict, (await editor.PutAsJsonAsync("/api/editorial/homepage", input)).StatusCode);
            PublicHomepageResponse home = (await anonymous.GetFromJsonAsync<PublicHomepageResponse>("/api/news/homepage"))!;
            Assert.Equal(new[] { first.Id, second.Id }, home.Categories.Select(item => item.Category.Id));
            Assert.All(home.Categories, item => Assert.Empty(item.Articles));
            Assert.Equal(1L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"CurationChanges\" WHERE \"Action\"='homepage-categories' AND @id IS NOT NULL", first.Id));
            editor.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync("/api/editorial/homepage", new { changed.Version, firstCategoryId = second.Id, secondCategoryId = first.Id })).StatusCode);
        }
    }

    [Fact]
    public async Task Curation_ConcurrentFeaturedLimitAndWithdrawalPreserveVisibilityAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Featured");
            var articles = new List<ArticleResponse>();
            for (int i = 0; i < 6; i++)
                articles.Add(await PublishedAsync(owner, editor, category.Id, "Featured " + i));
            for (int i = 0; i < 4; i++)
                Assert.Equal(HttpStatusCode.OK, (await editor.PutAsJsonAsync($"/api/editorial/articles/{articles[i].Id}/featured", new { articles[i].Version, featured = true })).StatusCode);
            HttpResponseMessage[] race = await Task.WhenAll(articles.Skip(4).Select(item => editor.PutAsJsonAsync($"/api/editorial/articles/{item.Id}/featured", new { item.Version, featured = true })));
            Assert.Single(race, item => item.StatusCode == HttpStatusCode.OK);
            Assert.Single(race, item => item.StatusCode == HttpStatusCode.BadRequest);
            PublicHomepageResponse home = (await anonymous.GetFromJsonAsync<PublicHomepageResponse>("/api/news/homepage"))!;
            Assert.Equal(5, home.Featured.Length);
            Assert.Equal(5, home.Latest.Length);
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/editorial/articles/{articles[0].Id}/featured", new { version = articles[0].Version + 1, featured = false })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await editor.PutAsJsonAsync($"/api/editorial/articles/{articles[0].Id}/featured", new { articles[0].Version, featured = false })).StatusCode);
            ArticleResponse current = (await editor.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{articles[0].Id}/publication"))!;
            await ActAsync(editor, current, "withdraw", "Withdraw selection");
            home = (await anonymous.GetFromJsonAsync<PublicHomepageResponse>("/api/news/homepage"))!;
            Assert.Equal(4, home.Featured.Length);
            Assert.DoesNotContain(home.Featured.Concat(home.Latest), item => item.Id == current.Id);
            Assert.False(await ScalarAsync<bool>(database, "SELECT \"Featured\" FROM news.\"Articles\" WHERE \"Id\"=@id", current.Id));
            Assert.Equal(5L, await ScalarAsync<long>(database, "SELECT count(*) FROM news.\"CurationChanges\" WHERE \"Action\"='feature' AND @id IS NOT NULL", current.Id));
            foreach (HttpResponseMessage response in race)
                response.Dispose();
        }
    }
}
