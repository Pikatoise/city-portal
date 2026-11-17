using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using CityPortal.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    [Fact]
    public async Task DiscoveryMigration_ExistingPublishedSearchData_PreservesContentAndUsesMainStorageAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        (_, HttpClient author) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (author)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Upgrade category");
            ArticleResponse article = await PublishedAsync(author, editor, category.Id, "Городские библиотеки");
            string expectedContent = await ScalarAsync<string>(database, "SELECT \"ContentJson\"::text FROM news.\"Revisions\" WHERE \"Id\"=@id", article.RevisionId);
            // Recreate the preceding migration's physical policy without changing business data.
            await ExecuteAsync(database, """
                DROP INDEX news."IX_Articles_PublishedRevisionId";
                ALTER TABLE news."Revisions" ALTER COLUMN "SearchVector" SET STORAGE EXTENDED;
                DELETE FROM news."__EFMigrationsHistory" WHERE "MigrationId"='20261117072429_DiscoveryReadPerformance';
                """, article.Id);
            await factory.Services.MigrateNewsAsync(CancellationToken.None);
            await factory.Services.MigrateNewsAsync(CancellationToken.None);
            Assert.Equal(expectedContent, await ScalarAsync<string>(database, "SELECT \"ContentJson\"::text FROM news.\"Revisions\" WHERE \"Id\"=@id", article.RevisionId));
            Assert.Equal(article.Version, (await author.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!.Version);
            Assert.Equal(article.Id, Assert.Single((await DiscoveryAsync(anonymous, "?query=" + Uri.EscapeDataString("библиотека"))).Items).Id);
            Assert.Equal("m", await ScalarAsync<string>(database, """SELECT attstorage::text FROM pg_attribute WHERE attrelid='news."Revisions"'::regclass AND attname='SearchVector'""", article.Id));
            Assert.True(await ScalarAsync<bool>(database, """SELECT to_regclass('news."IX_Articles_PublishedRevisionId"') IS NOT NULL""", article.Id));
            Assert.Equal(1L, await ScalarAsync<long>(database, """SELECT count(*) FROM news."__EFMigrationsHistory" WHERE "MigrationId"='20261117072429_DiscoveryReadPerformance'""", article.Id));
        }
    }

    [Fact]
    public async Task Recovery_DatabaseObjectsAndKeys_PreserveNewsPrivacyHistoryAndPendingDeliveryAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "cityportal-recovery-" + Guid.NewGuid().ToString("N"));
        string sourceKeys = Path.Combine(root, "source-keys");
        string restoredKeys = Path.Combine(root, "restored-keys");
        Directory.CreateDirectory(sourceKeys);
        Directory.CreateDirectory(restoredKeys);
        try
        {
            (ApiFactory source, string database, _) = await _fixture.CreateAsync(new Dictionary<string, string>
            {
                ["Security:DataProtectionKeysPath"] = sourceKeys
            });
            await using ApiFactory sourceLifetime = source;
            await using var smtp = new DeliveryIntegrationTests.TestSmtp();
            (_, HttpClient author) = await ActorAsync(source, database, "Author");
            (_, HttpClient editor) = await ActorAsync(source, database, "Editor");
            using (author)
            using (editor)
            {
                TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Recovery category");
                ImageResponse publicImage = await UploadAsync(author);
                ImageResponse privateImage = await UploadAsync(author);
                await ProcessImagesAsync(source);
                ArticleResponse article = await PublishedAsync(author, editor, category.Id, "Restored public news", new ArticleImage(publicImage.Id, "Public river"));
                ArticleResponse proposal = await ActAsync(author, article, "working-version");
                proposal = await SaveAsync(author, proposal, Complete(category.Id, "PRIVATE recovery proposal") with { Cover = new ArticleImage(privateImage.Id, "Private river") });
                ImageResponse pending = await UploadAsync(author);
                var expectedImages = new Dictionary<string, byte[]>();
                foreach (string variant in new[] { "master", "480", "1280", "2560" })
                    expectedImages[variant] = await author.GetByteArrayAsync($"/api/media/images/{publicImage.Id}/{variant}");
                AccountResponse account = (await author.GetFromJsonAsync<AccountResponse>("/api/account"))!;
                Guid deliveryId = await ScalarAsync<Guid>(database, "SELECT \"Id\" FROM notifications.\"EmailDeliveries\" WHERE \"AccountId\"=@id AND \"NotificationType\"='article.published'", account.Id);
                await using (var connection = new NpgsqlConnection(database))
                {
                    await connection.OpenAsync();
                    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
                    await using AsyncServiceScope scope = source.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<MailConfigurationStore>().SaveAsync(new AccountMailOptions
                    {
                        SmtpHost = "127.0.0.1",
                        SmtpPort = smtp.Port,
                        UseTls = false,
                        PublicOrigin = "https://recovery.cityportal.test",
                        PortalName = "Recovery city",
                        SenderAddress = "recovery@cityportal.test",
                        Password = "protected-fixture-secret"
                    }, connection, transaction, CancellationToken.None);
                    await transaction.CommitAsync();
                }

                // No hosted consumer or background writer runs during this backup.
                foreach (string file in Directory.GetFiles(sourceKeys))
                    File.Copy(file, Path.Combine(restoredKeys, Path.GetFileName(file)));
                Assert.NotEmpty(Directory.GetFiles(restoredKeys, "key-*.xml"));
                await using ApiFactory restored = await _fixture.RestoreAsync(source, database, restoredKeys, objectPageSize: 2);
                string restoredDatabase = restored.Services.GetRequiredService<IConfiguration>().GetConnectionString("Database")!;
                Assert.NotEqual(database, restoredDatabase);
                await source.DisposeAsync();
                await restored.Services.MigrateIdentityAsync(CancellationToken.None);
                await restored.Services.MigrateNotificationsAsync(CancellationToken.None);
                await restored.Services.MigrateNewsAsync(CancellationToken.None);
                await restored.Services.MigrateMediaAsync(CancellationToken.None);
                await restored.Services.MigrateAccountMailMessagingAsync();

                using HttpClient anonymous = Client(restored);
                using HttpClient restoredAuthor = Client(restored);
                using JsonDocument visible = await PublicAsync(anonymous, article.Id);
                Assert.Equal("Restored public news", visible.RootElement.GetProperty("content").GetProperty("title").GetString());
                Assert.DoesNotContain("PRIVATE recovery proposal", await anonymous.GetStringAsync("/api/news/articles"));
                foreach (KeyValuePair<string, byte[]> image in expectedImages)
                    Assert.Equal(image.Value, await anonymous.GetByteArrayAsync($"/api/media/images/{publicImage.Id}/{image.Key}"));
                Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{privateImage.Id}/480")).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/editorial/articles/{article.Id}/history")).StatusCode);
                await CsrfAsync(restoredAuthor);
                Assert.Equal(HttpStatusCode.OK, (await restoredAuthor.PostAsJsonAsync("/api/auth/login", new { account.Email, password = _password, rememberMe = false })).StatusCode);
                await CsrfAsync(restoredAuthor);
                ArticleResponse recoveredProposal = (await restoredAuthor.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"))!;
                Assert.Equal(proposal.RevisionId, recoveredProposal.RevisionId);
                Assert.Equal("PRIVATE recovery proposal", recoveredProposal.Content.Title);
                Assert.Equal(HttpStatusCode.OK, (await restoredAuthor.GetAsync($"/api/media/images/{privateImage.Id}/480")).StatusCode);
                NewsPage<ArticleHistoryResponse> history = (await restoredAuthor.GetFromJsonAsync<NewsPage<ArticleHistoryResponse>>($"/api/editorial/articles/{article.Id}/history"))!;
                Assert.Contains(history.Items, item => item.Action == "publish" && item.Revision.Content.Title == "Restored public news");
                Assert.Equal("Processing", (await restoredAuthor.GetFromJsonAsync<ImageResponse>($"/api/media/images/{pending.Id}"))!.State);
                await ProcessImagesAsync(restored);
                Assert.Equal("Ready", (await restoredAuthor.GetFromJsonAsync<ImageResponse>($"/api/media/images/{pending.Id}"))!.State);
                Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/images/{pending.Id}/480")).StatusCode);
                await using (AsyncServiceScope scope = restored.Services.CreateAsyncScope())
                {
                    AccountMailOptions configuration = await scope.ServiceProvider.GetRequiredService<MailConfigurationStore>().GetAsync(CancellationToken.None);
                    Assert.Equal("protected-fixture-secret", configuration.Password);
                    Assert.Equal("Recovery city", configuration.PortalName);
                }
                IConfiguration configurationRoot = restored.Services.GetRequiredService<IConfiguration>();
                string messaging = configurationRoot.GetConnectionString("Messaging")!;
                using IHost worker = Host.CreateDefaultBuilder().ConfigureServices(services =>
                {
                    services.AddIdentityModule(restoredDatabase).AddNotificationsModule(restoredDatabase, messaging);
                    services.AddDataProtection().SetApplicationName("CityPortal").PersistKeysToFileSystem(new DirectoryInfo(restoredKeys));
                    services.AddAccountMailWorker(configurationRoot, restoredDatabase);
                }).UseAccountMailMessaging(restoredDatabase, messaging, isWorker: false).Build();
                await worker.StartAsync();
                await using (AsyncServiceScope scope = worker.Services.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<AccountMailDeliveryService>().DeliverAsync(new DeliverAccountMail { DeliveryId = deliveryId, Attempt = 0 }, CancellationToken.None);
                await worker.StopAsync();
                Assert.Equal("Sent", await ScalarAsync<string>(restoredDatabase, "SELECT \"State\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", deliveryId));
                Assert.Equal("", await ScalarAsync<string>(restoredDatabase, "SELECT \"ProtectedPayload\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", deliveryId));
                Assert.Single(smtp.Accepted);
                Assert.Contains("recovery@cityportal.test", smtp.Accepted.Single());
                Assert.Contains("article.published", await restoredAuthor.GetStringAsync("/api/account/notifications"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
