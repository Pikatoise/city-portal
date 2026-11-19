using System.Net;
using CityPortal.Media;
using CityPortal.News;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Fact]
    public async Task Review_MediaVisibilityUpdatesOnlyChangedReferencesAndRollsBackAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        using HttpClient author = owner;
        (_, HttpClient first) = await ActorAsync(factory, database, "Editor");
        using HttpClient reviewer = first;
        (_, HttpClient second) = await ActorAsync(factory, database, "Editor");
        using HttpClient replacement = second;
        TaxonomyResponse category = await TaxonomyAsync(reviewer, "categories", "Visibility changes");
        ImageResponse image = await UploadAsync(author);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MediaMaintenance>().ProcessPendingAsync(CancellationToken.None);
        ArticleResponse draft = await SaveAsync(author, await CreateAsync(author),
            Complete(category.Id, "Image audience") with { Cover = new ArticleImage(image.Id, "River") });
        ArticleResponse article = await ActAsync(author, draft, "submit");
        await InstallReferenceWriteAuditAsync(database);
        string imagePath = $"/api/media/images/{image.Id}/480";

        article = await ActAsync(reviewer, article, "claim");
        Assert.Equal(1L, await ScalarAsync<long>(database,
            """SELECT count(*) FROM media."ReferenceWriteAudit" WHERE "Operation"='UPDATE' AND "RevisionId"=@id""", article.RevisionId));
        Assert.Equal(1L, await ScalarAsync<long>(database,
            """SELECT count(*) FROM media."ReferenceWriteAudit" WHERE "ImageId"=@id""", image.Id));
        using HttpResponseMessage granted = await reviewer.GetAsync(imagePath);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        await ExecuteAsync(database, """ALTER TABLE news."History" ADD CONSTRAINT reject_takeover CHECK ("Action" <> 'takeover')""", article.Id);
        using HttpResponseMessage failed = await ActResponseAsync(replacement, article, "takeover");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        using HttpResponseMessage retained = await reviewer.GetAsync(imagePath);
        using HttpResponseMessage denied = await replacement.GetAsync(imagePath);
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(1L, await ScalarAsync<long>(database,
            """SELECT count(*) FROM media."ReferenceWriteAudit" WHERE "ImageId"=@id""", image.Id));

        await ExecuteAsync(database, """ALTER TABLE news."History" DROP CONSTRAINT reject_takeover""", article.Id);
        ArticleResponse taken = await ActAsync(replacement, article, "takeover");
        Assert.Equal(article.Version + 1, taken.Version);
        using HttpResponseMessage lost = await reviewer.GetAsync(imagePath);
        using HttpResponseMessage gained = await replacement.GetAsync(imagePath);
        Assert.Equal(HttpStatusCode.NotFound, lost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gained.StatusCode);
        Assert.Equal(2L, await ScalarAsync<long>(database,
            """SELECT count(*) FROM media."ReferenceWriteAudit" WHERE "Operation"='UPDATE' AND "RevisionId"=@id""", article.RevisionId));
        Assert.Equal(2L, await ScalarAsync<long>(database,
            """SELECT count(*) FROM media."ReferenceWriteAudit" WHERE "ImageId"=@id""", image.Id));

        await ActAsync(replacement, taken, "release");
        using HttpResponseMessage released = await replacement.GetAsync(imagePath);
        Assert.Equal(HttpStatusCode.NotFound, released.StatusCode);
        Assert.Equal(3L, await ScalarAsync<long>(database,
            """SELECT count(*) FROM media."ReferenceWriteAudit" WHERE "Operation"='UPDATE' AND "RevisionId"=@id""", article.RevisionId));
        Assert.Equal(3L, await ScalarAsync<long>(database,
            """SELECT count(*) FROM media."ReferenceWriteAudit" WHERE "ImageId"=@id""", image.Id));
    }

    private static Task InstallReferenceWriteAuditAsync(string database) => ExecuteAsync(database, """
        CREATE TABLE media."ReferenceWriteAudit" ("Operation" text NOT NULL, "RevisionId" uuid NOT NULL, "ImageId" uuid NOT NULL);
        CREATE FUNCTION media.audit_reference_write() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            INSERT INTO media."ReferenceWriteAudit"
                VALUES (TG_OP, COALESCE(NEW."RevisionId", OLD."RevisionId"), COALESCE(NEW."ImageId", OLD."ImageId"));
            RETURN NULL;
        END
        $$;
        CREATE TRIGGER audit_reference_write AFTER INSERT OR UPDATE OR DELETE ON media."References"
            FOR EACH ROW EXECUTE FUNCTION media.audit_reference_write();
        """, Guid.Empty);
}
