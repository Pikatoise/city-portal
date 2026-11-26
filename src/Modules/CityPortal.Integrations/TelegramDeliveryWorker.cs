using CityPortal.Identity;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.Integrations;

public sealed class TelegramDeliveryWorker
{
    private readonly IntegrationDbContext _context;
    private readonly PublicArticleOperations _articles;
    private readonly TelegramBotClient _bot;
    private readonly IDbContextOutbox _outbox;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<TelegramDeliveryWorker> _logger;

    public TelegramDeliveryWorker(IntegrationDbContext context, PublicArticleOperations articles, TelegramBotClient bot,
        IDbContextOutbox outbox, IConfiguration configuration, TimeProvider time, ILogger<TelegramDeliveryWorker> logger)
    {
        _context = context;
        _articles = articles;
        _bot = bot;
        _outbox = outbox;
        _configuration = configuration;
        _time = time;
        _logger = logger;
    }

    public async Task DeliverAsync(TelegramDeliveryMessage message, CancellationToken cancellationToken)
    {
        string? token = _configuration["Integrations:Telegram:BotToken"];
        Uri? origin = IntegrationOrigin.FromConfiguration(_configuration);
        TelegramDelivery? delivery;
        string articleUrl;
        await using (IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
        {
            // Serialize the dispatch decision with publication changes and administrative updates.
            await AccountAuthorization.LockRolesAsync(_context, shared: false, cancellationToken);
            delivery = await FindAsync(message.DeliveryId, cancellationToken);
            if (delivery is null || delivery.Attempt != message.Attempt || delivery.State is not ("Queued" or "RetryScheduled"))
                return;

            IntegrationSettings? settings = await TelegramWorkflow.SettingsAsync(_context, cancellationToken);
            PublicArticleAnnouncement? article = await _articles.FindAnnouncementAsync(delivery.ArticleId, transaction.GetDbTransaction(), cancellationToken);
            if (settings?.TelegramEnabled != true || TelegramWorkflow.Channel(settings) != delivery.Channel || !TelegramBotClient.IsValidToken(token) || origin is null
                || article is null || article.RevisionId != delivery.RevisionId)
            {
                TelegramWorkflow.Change(_context, delivery, "Cancelled", "telegram_dispatch_changed", "Cancelled", _time.GetUtcNow());
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                TelegramMetrics.Outcomes.Add(1, new KeyValuePair<string, object?>("state", "Cancelled"));
                return;
            }

            articleUrl = new Uri(origin, article.PublicAddress).AbsoluteUri;
            // A restart after this commit cannot tell whether Telegram accepted the post.
            TelegramWorkflow.Change(_context, delivery, "Sending", null, "DispatchStarted", _time.GetUtcNow());
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        TelegramMetrics.Dispatches.Add(1);
        TelegramSendResult result = await _bot.SendAsync(token!, delivery.Channel, delivery.Text, articleUrl, cancellationToken);
        _context.ChangeTracker.Clear();
        await CompleteAsync(message, result, cancellationToken);
    }

    private async Task CompleteAsync(TelegramDeliveryMessage message, TelegramSendResult result, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AccountAuthorization.LockRolesAsync(_context, shared: false, cancellationToken);
        TelegramDelivery? delivery = await FindAsync(message.DeliveryId, cancellationToken);
        if (delivery is null || delivery.Attempt != message.Attempt || delivery.State != "Sending")
            return;

        DateTimeOffset now = _time.GetUtcNow();
        bool retry = result.RetryAfterSeconds is not null && delivery.RateLimitRetries < 5;
        string state;
        if (result.MessageId is int messageId)
        {
            delivery.MessageId = messageId;
            state = "Sent";
        }
        else if (retry)
        {
            state = "RetryScheduled";
            delivery.Attempt++;
            delivery.RateLimitRetries++;
            _outbox.Enroll(_context);
            await _outbox.ScheduleAsync(new TelegramDeliveryMessage(delivery.Id, delivery.Attempt), now.AddSeconds(result.RetryAfterSeconds!.Value));
        }
        else
            state = result.OutcomeUnknown ? "NeedsReview" : "Failed";

        TelegramWorkflow.Change(_context, delivery, state, result.ErrorCode, state, now);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TelegramMetrics.Outcomes.Add(1, new KeyValuePair<string, object?>("state", state));
        if (retry)
            await _outbox.FlushOutgoingMessagesAsync();
        if (result.ErrorCode is not null)
            _logger.LogWarning("Telegram delivery {DeliveryId} reached {State} with code {ErrorCode}.", delivery.Id, state, result.ErrorCode);
    }

    private Task<TelegramDelivery?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Set<TelegramDelivery>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
}
