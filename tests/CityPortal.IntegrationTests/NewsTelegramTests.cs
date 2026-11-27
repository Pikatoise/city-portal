using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Api;
using CityPortal.Api.Contracts;
using CityPortal.Identity;
using CityPortal.Integrations;
using CityPortal.News;
using CityPortal.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    private static readonly Dictionary<string, string> _telegramConfiguration = new()
    {
        ["PublicOrigin"] = "https://city.example",
        ["Integrations:Telegram:BotToken"] = "1234:fixture_only"
    };

    [Fact]
    public async Task Telegram_QueuesCurrentPublicRevision_AndReconcilesLostResponseWithoutSendingTwiceAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync(_telegramConfiguration);
        await using ApiFactory lifetime = factory;
        (_, HttpClient author) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (author)
        using (editor)
        using (HttpClient anonymous = Client(factory))
        {
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Telegram");
            ArticleResponse article = await PublishDiscoveryAsync(author, editor, Complete(category.Id, "Public Telegram article"));
            string path = $"/api/editorial/articles/{article.Id}/telegram";
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await author.GetAsync(path)).StatusCode);
            Assert.False((await TelegramPreviewAsync(editor, path)).Enabled);

            await EnableTelegramAsync(factory);
            TelegramAnnouncementResponse preview = await TelegramPreviewAsync(editor, path);
            Assert.True(preview.Enabled);
            Assert.False(preview.AiEnabled);
            Assert.Equal("https://city.example" + article.PublicAddress, preview.ArticleUrl);
            Assert.Equal("@city_fixture", preview.Channel);
            Assert.Null(preview.Delivery);

            var send = new QueueTelegramAnnouncementRequest(preview.RevisionId!.Value, "@city_fixture", "  Verified announcement  ");
            editor.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync(path, send)).StatusCode);
            await CsrfAsync(editor);
            Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync(path, send with { RevisionId = Guid.NewGuid() })).StatusCode);
            using HttpResponseMessage queued = await editor.PostAsJsonAsync(path, send);
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            Assert.EndsWith(path, queued.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync(path, send)).StatusCode);

            TelegramDeliveryMessage message = await TelegramMessageAsync(database, article.Id);
            var handler = new TelegramTestHandler(_ => throw new HttpRequestException("Response lost after write"));
            await DeliverTelegramAsync(factory, message, handler);
            await DeliverTelegramAsync(factory, message, handler);
            Assert.Equal(1, handler.Calls);
            Assert.Equal($"Verified announcement\n\n{preview.ArticleUrl}", handler.Text);
            Assert.Equal("@city_fixture", handler.Channel);
            TelegramDeliveryResponse unknown = Assert.IsType<TelegramDeliveryResponse>((await TelegramPreviewAsync(editor, path)).Delivery);
            Assert.Equal("NeedsReview", unknown.State);
            Assert.Equal("telegram_delivery_unknown", unknown.ErrorCode);
            Assert.Null(unknown.PostUrl);

            await ActAsync(editor, article, "withdraw", "Article withdrawn before reconciliation");
            TelegramRecoveryResponse[] recovery = Assert.IsType<TelegramRecoveryResponse[]>(
                await editor.GetFromJsonAsync<TelegramRecoveryResponse[]>("/api/editorial/telegram/recovery"));
            TelegramRecoveryResponse entry = Assert.Single(recovery);
            Assert.Equal(article.Id, entry.ArticleId);
            Assert.Equal("@city_fixture", entry.Channel);
            Assert.Null((await TelegramPreviewAsync(editor, path + "?channel=%40city_fixture")).RevisionId);

            var attach = new ResolveTelegramAnnouncementRequest("@city_fixture", unknown.Version, false, "https://t.me/other_city/88", null, null);
            using HttpResponseMessage wrongChannel = await editor.PostAsJsonAsync(path + "/resolve", attach);
            Assert.Equal(HttpStatusCode.BadRequest, wrongChannel.StatusCode);
            using HttpResponseMessage resolved = await editor.PostAsJsonAsync(path + "/resolve", attach with { PostUrl = "https://t.me/city_fixture/88" });
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
            TelegramDeliveryResponse linked = Assert.IsType<TelegramDeliveryResponse>(await resolved.Content.ReadFromJsonAsync<TelegramDeliveryResponse>());
            Assert.Equal("Sent", linked.State);
            Assert.Equal("https://t.me/city_fixture/88", linked.PostUrl);
            Assert.Empty(Assert.IsType<TelegramRecoveryResponse[]>(await editor.GetFromJsonAsync<TelegramRecoveryResponse[]>("/api/editorial/telegram/recovery")));
            Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync(path, send)).StatusCode);
        }
    }

    [Fact]
    public async Task Telegram_ThrottledAttemptSchedulesRetry_AndWithdrawalPreventsDispatchAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync(_telegramConfiguration);
        await using ApiFactory lifetime = factory;
        (_, HttpClient author) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (author)
        using (editor)
        {
            await EnableTelegramAsync(factory);
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Delivery retry");
            ArticleResponse article = await PublishDiscoveryAsync(author, editor, Complete(category.Id, "Article withdrawn during retry"));
            string path = $"/api/editorial/articles/{article.Id}/telegram";
            TelegramAnnouncementResponse preview = await TelegramPreviewAsync(editor, path);
            var send = new QueueTelegramAnnouncementRequest(preview.RevisionId!.Value, "@city_fixture", "Article announcement");
            Assert.Equal(HttpStatusCode.Accepted, (await editor.PostAsJsonAsync(path, send)).StatusCode);
            TelegramDeliveryMessage first = await TelegramMessageAsync(database, article.Id);
            var handler = new TelegramTestHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = JsonContent.Create(new { ok = false, parameters = new { retry_after = 23 } })
            });
            await DeliverTelegramAsync(factory, first, handler);
            TelegramDeliveryMessage scheduled = await TelegramMessageAsync(database, article.Id);
            Assert.Equal(first with { Attempt = first.Attempt + 1 }, scheduled);
            TelegramDeliveryResponse waiting = Assert.IsType<TelegramDeliveryResponse>((await TelegramPreviewAsync(editor, path)).Delivery);
            Assert.Equal("RetryScheduled", waiting.State);
            Assert.Equal("telegram_rate_limited", waiting.ErrorCode);

            await ActAsync(editor, article, "withdraw", "Confirmed publication problem");
            await DeliverTelegramAsync(factory, scheduled, handler);
            Assert.Equal(1, handler.Calls);
            TelegramAnnouncementResponse afterWithdrawal = await TelegramPreviewAsync(editor, path);
            Assert.Equal("Cancelled", afterWithdrawal.Delivery?.State);
            Assert.Equal("telegram_dispatch_changed", afterWithdrawal.Delivery?.ErrorCode);
            Assert.Null(afterWithdrawal.RevisionId);
        }
    }

    [Fact]
    public async Task Telegram_AiReadsOnlyCurrentPublicTitleAndSummary_AndManualQueueSurvivesInvalidOutputAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync(_telegramConfiguration);
        await using ApiFactory lifetime = factory;
        (_, HttpClient author) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (author)
        using (editor)
        {
            await EnableTelegramAsync(factory, ai: true);
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "AI announcements");
            ArticleResponse article = await PublishDiscoveryAsync(author, editor,
                Complete(category.Id, "Public AI article") with { Summary = "Public AI summary" });
            ArticleResponse working = await ActAsync(author, article, "working-version");
            await SaveAsync(author, working, Complete(category.Id, "Private draft title") with { Summary = "Private draft summary" });

            var chat = new AnnouncementChatStub { Reply = """{"text":"Suggested text"}""" };
            await using WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IChatClient>(chat)));
            using HttpClient aiEditor = configured.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            AccountResponse profile = Assert.IsType<AccountResponse>(await editor.GetFromJsonAsync<AccountResponse>("/api/account"));
            await CsrfAsync(aiEditor);
            Assert.Equal(HttpStatusCode.OK, (await aiEditor.PostAsJsonAsync("/api/auth/login", new { email = profile.Email, password = _password, rememberMe = false })).StatusCode);
            await CsrfAsync(aiEditor);

            string path = $"/api/editorial/articles/{article.Id}/telegram";
            TelegramAnnouncementResponse preview = await TelegramPreviewAsync(aiEditor, path);
            Assert.True(preview.AiEnabled);
            var suggest = new SuggestTelegramAnnouncementRequest(preview.RevisionId!.Value);
            using HttpResponseMessage proposed = await aiEditor.PostAsJsonAsync(path + "/suggest", suggest);
            Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
            Assert.Equal("Suggested text", (await proposed.Content.ReadFromJsonAsync<TelegramSuggestionResponse>())?.Text);
            using var facts = JsonDocument.Parse(Assert.Single(chat.Messages, message => message.Role == ChatRole.User).Text);
            Assert.Equal("Public AI article", facts.RootElement.GetProperty("title").GetString());
            Assert.Equal("Public AI summary", facts.RootElement.GetProperty("summary").GetString());
            Assert.Collection(facts.RootElement.EnumerateObject(), field => Assert.Equal("title", field.Name), field => Assert.Equal("summary", field.Name));
            Assert.Equal(400, chat.Options?.MaxOutputTokens);
            Assert.Null((await TelegramPreviewAsync(aiEditor, path)).Delivery);

            chat.Reply = """{"text":" "}""";
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await aiEditor.PostAsJsonAsync(path + "/suggest", suggest)).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted,
                (await aiEditor.PostAsJsonAsync(path, new QueueTelegramAnnouncementRequest(suggest.RevisionId, "@city_fixture", "Manual announcement"))).StatusCode);
            Assert.Equal("Manual announcement", (await TelegramPreviewAsync(aiEditor, path)).Delivery?.Text);
        }
    }

    [Fact]
    public async Task Telegram_DeliversAnnouncementQueuedDuringBrokerOutage_WhenWorkerStartsAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync(_telegramConfiguration);
        await using ApiFactory lifetime = factory;
        (_, HttpClient author) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (author)
        using (editor)
        {
            await EnableTelegramAsync(factory);
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "Broker delivery");
            ArticleResponse article = await PublishDiscoveryAsync(author, editor, Complete(category.Id, "Queued for RabbitMQ Worker"));
            string path = $"/api/editorial/articles/{article.Id}/telegram";
            TelegramAnnouncementResponse preview = await TelegramPreviewAsync(editor, path);
            await _fixture.SetBrokerRunningAsync(false);
            try
            {
                using HttpResponseMessage queued = await editor.PostAsJsonAsync(path,
                    new QueueTelegramAnnouncementRequest(preview.RevisionId!.Value, "@city_fixture", "Durable from RabbitMQ"));
                Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
                Assert.Equal("Queued", (await TelegramPreviewAsync(editor, path)).Delivery?.State);
            }
            finally
            {
                await _fixture.SetBrokerRunningAsync(true);
            }

            string messaging = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Messaging")!;
            using var handler = new TelegramTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { ok = true, result = new { message_id = 52 } })
            });
            using var botHttp = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://telegram.invalid") };
            using IHost worker = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(_telegramConfiguration.Select(item => new KeyValuePair<string, string?>(item.Key, item.Value))))
                .ConfigureServices((context, services) =>
                {
                    services.AddDataProtection().SetApplicationName("CityPortal");
                    services.AddSingleton(TimeZoneInfo.Utc);
                    services.AddIdentityModule(database).AddNotificationsModule(database, messaging).AddNewsModule(database);
                    services.AddIntegrationsModule(database).AddTelegramIntegrations(context.Configuration);
                    services.AddAccountMailWorker(context.Configuration, database);
                    services.AddSingleton(new TelegramBotClient(botHttp));
                })
                .UseAccountMailMessaging(database, messaging, isWorker: true, options => options.ConfigureTelegramMessaging(isWorker: true))
                .Build();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await worker.StartAsync(deadline.Token);
            try
            {
                TelegramDeliveryResponse? delivery;
                do
                {
                    await Task.Delay(100, deadline.Token);
                    delivery = (await TelegramPreviewAsync(editor, path)).Delivery;
                }
                while (delivery?.State != "Sent");
                Assert.Equal(1, handler.Calls);
                Assert.Equal(new Uri("https://telegram.invalid/bot1234:fixture_only/sendMessage"), handler.RequestUri);
                Assert.Equal("Durable from RabbitMQ\n\n" + preview.ArticleUrl, handler.Text);
                Assert.Equal("https://t.me/city_fixture/52", delivery.PostUrl);
            }
            finally
            {
                await worker.StopAsync();
            }
        }
    }

    private static async Task EnableTelegramAsync(ApiFactory factory, bool ai = false)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IntegrationDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        IntegrationSettings settings = await context.Settings.SingleAsync();
        settings.TelegramEnabled = true;
        settings.TelegramChannel = "@city_fixture";
        settings.AiEnabled = ai;
        await context.SaveChangesAsync();
    }

    private static async Task<TelegramAnnouncementResponse> TelegramPreviewAsync(HttpClient editor, string path) =>
        Assert.IsType<TelegramAnnouncementResponse>(await editor.GetFromJsonAsync<TelegramAnnouncementResponse>(path));

    private static async Task<TelegramDeliveryMessage> TelegramMessageAsync(string database, Guid articleId)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT "Id", "Attempt" FROM integrations."TelegramDeliveries" WHERE "ArticleId" = @article
            """, connection);
        command.Parameters.AddWithValue("article", articleId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var message = new TelegramDeliveryMessage(reader.GetGuid(0), reader.GetInt32(1));
        Assert.False(await reader.ReadAsync());
        return message;
    }

    private static async Task DeliverTelegramAsync(ApiFactory factory, TelegramDeliveryMessage message, HttpMessageHandler handler)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://telegram.invalid") };
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        var worker = new TelegramDeliveryWorker(services.GetRequiredService<IntegrationDbContext>(), services.GetRequiredService<PublicArticleOperations>(),
            new TelegramBotClient(http), services.GetRequiredService<IDbContextOutbox>(), services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILogger<TelegramDeliveryWorker>>());
        await worker.DeliverAsync(message, CancellationToken.None);
    }

    private sealed class TelegramTestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public TelegramTestHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public int Calls { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Text { get; private set; }

        public string? Channel { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Text = body.RootElement.GetProperty("text").GetString();
            Channel = body.RootElement.GetProperty("chat_id").GetString();
            return _response(request);
        }
    }

    private sealed class AnnouncementChatStub : IChatClient
    {
        public string Reply { get; set; } = "";

        public ChatMessage[] Messages { get; private set; } = [];

        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
