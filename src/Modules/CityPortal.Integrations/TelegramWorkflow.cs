using System.Data.Common;
using CityPortal.Identity;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.Integrations;

public sealed class TelegramWorkflow
{
    private static readonly string[] _roles = ["Editor", "Administrator"];
    private readonly IntegrationDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly PublicArticleOperations _articles;
    private readonly IDbContextOutbox _outbox;
    private readonly TelegramAnnouncementAi _ai;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    public TelegramWorkflow(IntegrationDbContext context, AccountAuthorization authorization, PublicArticleOperations articles,
        IDbContextOutbox outbox, TelegramAnnouncementAi ai, IConfiguration configuration, TimeProvider time)
    {
        _context = context;
        _authorization = authorization;
        _articles = articles;
        _outbox = outbox;
        _ai = ai;
        _configuration = configuration;
        _time = time;
    }

    public async Task<TelegramAnnouncementResponse> GetAsync(Guid actorId, Guid sessionId, Guid articleId, string? selectedChannel, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        IntegrationSettings? settings = await SettingsAsync(_context, cancellationToken);
        string? channel = Channel(settings);
        PublicArticleAnnouncement? article = await _articles.FindAnnouncementAsync(articleId, null, cancellationToken);
        TelegramDelivery? delivery = await _context.Set<TelegramDelivery>().AsNoTracking()
            .Where(item => item.ArticleId == articleId && (selectedChannel == null || item.Channel == selectedChannel))
            .OrderByDescending(item => item.State == "NeedsReview" || item.State == "Sending")
            .ThenByDescending(item => item.Channel == channel).ThenByDescending(item => item.UpdatedAtUtc).ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (article is null && delivery is null)
            throw UnavailableArticle();

        Uri? origin = IntegrationOrigin.FromConfiguration(_configuration);
        bool enabled = settings?.TelegramEnabled == true && channel is not null && TokenConfigured && origin is not null;
        return new TelegramAnnouncementResponse(article?.RevisionId, article is null ? null : $"{article.Title}\n\n{article.Summary}",
            article is null || origin is null ? null : new Uri(origin, article.PublicAddress).AbsoluteUri,
            enabled, enabled && settings?.AiEnabled == true && _ai.Available,
            channel, delivery is null ? null : Describe(delivery));
    }

    public async Task<TelegramRecoveryResponse[]> RecoveryAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        DateTimeOffset cutoff = _time.GetUtcNow().AddMinutes(-2);
        return await _context.Set<TelegramDelivery>().AsNoTracking()
            .Where(item => item.State == "NeedsReview" || (item.State == "Sending" && item.UpdatedAtUtc <= cutoff))
            .OrderBy(item => item.UpdatedAtUtc).ThenBy(item => item.Id).Take(20)
            .Select(item => new TelegramRecoveryResponse(item.ArticleId, item.Channel, item.Text.Substring(0, Math.Min(item.Text.Length, 120)), item.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<TelegramDeliveryResponse> QueueAsync(Guid actorId, Guid sessionId, Guid articleId, Guid revisionId, string channel,
        string text, CancellationToken cancellationToken)
    {
        text = ValidateText(text);

        await using AuthorizedAccountScope scope = await BeginAsync(actorId, sessionId, cancellationToken);
        await RequireChannelAsync(channel, cancellationToken);
        await RequireCurrentAsync(articleId, revisionId, scope.Transaction, cancellationToken);
        TelegramDelivery? delivery = await _context.Set<TelegramDelivery>()
            .SingleOrDefaultAsync(item => item.ArticleId == articleId && item.Channel == channel, cancellationToken);
        if (delivery is not null && delivery.State is not ("Failed" or "Cancelled"))
            throw Conflict();
        if (delivery is null)
        {
            delivery = new TelegramDelivery { Id = Guid.NewGuid(), ArticleId = articleId, Channel = channel };
            _context.Set<TelegramDelivery>().Add(delivery);
        }

        delivery.RevisionId = revisionId;
        delivery.Text = text;
        ResetForQueue(delivery, actorId, "Queued");
        await SaveEnqueuedAsync(scope, delivery, cancellationToken);
        return Describe(delivery);
    }

    public async Task<TelegramDeliveryResponse> ResolveAsync(Guid actorId, Guid sessionId, Guid articleId, string channel, long version,
        bool retry, bool cancel, string? postUrl, Guid? revisionId, string? text, CancellationToken cancellationToken)
    {
        int decisions = (retry ? 1 : 0) + (cancel ? 1 : 0) + (string.IsNullOrWhiteSpace(postUrl) ? 0 : 1);
        if (decisions != 1)
            throw new AccountOperationException(400, "invalid_telegram_resolution", "Select one delivery resolution.");

        await using AuthorizedAccountScope scope = await BeginAsync(actorId, sessionId, cancellationToken);
        TelegramDelivery? delivery = await _context.Set<TelegramDelivery>()
            .SingleOrDefaultAsync(item => item.ArticleId == articleId && item.Channel == channel, cancellationToken);
        if (delivery is null || delivery.Version != version || !NeedsReview(delivery, _time.GetUtcNow()))
            throw Conflict();
        if (retry)
        {
            await RequireChannelAsync(channel, cancellationToken);
            await RequireCurrentAsync(articleId, revisionId ?? delivery.RevisionId, scope.Transaction, cancellationToken);
            if (revisionId is Guid current && current != delivery.RevisionId)
            {
                delivery.RevisionId = current;
                delivery.Text = ValidateText(text);
            }
            ResetForQueue(delivery, actorId, "ManualRetry");
            await SaveEnqueuedAsync(scope, delivery, cancellationToken);
        }
        else
        {
            if (cancel)
                Change(_context, delivery, "Cancelled", null, "ManuallyCancelled", _time.GetUtcNow(), actorId);
            else
            {
                delivery.MessageId = ReadPostId(postUrl!, channel);
                Change(_context, delivery, "Sent", null, "Linked", _time.GetUtcNow(), actorId);
            }
            await _context.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }

        return Describe(delivery);
    }

    public async Task<string> SuggestAsync(Guid actorId, Guid sessionId, Guid articleId, Guid revisionId, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        IntegrationSettings? settings = await SettingsAsync(_context, cancellationToken);
        if (settings?.TelegramEnabled != true || Channel(settings) is null || !TokenConfigured || IntegrationOrigin.FromConfiguration(_configuration) is null
            || !settings.AiEnabled || !_ai.Available)
            throw new AccountOperationException(409, "ai_unavailable", "Telegram announcement suggestions are disabled.");
        PublicArticleAnnouncement article = await RequireCurrentAsync(articleId, revisionId, null, cancellationToken);
        string text = await _ai.SuggestAsync(actorId, article.Title, article.Summary, cancellationToken);
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        await RequireCurrentAsync(articleId, revisionId, null, cancellationToken);
        return text;
    }

    internal static Task<IntegrationSettings?> SettingsAsync(IntegrationDbContext context, CancellationToken cancellationToken) =>
        context.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);

    internal static string? Channel(IntegrationSettings? settings) => settings?.TelegramChannel?.Trim().ToLowerInvariant();

    internal static bool NeedsReview(TelegramDelivery delivery, DateTimeOffset now) =>
        delivery.State == "NeedsReview" || (delivery.State == "Sending" && delivery.UpdatedAtUtc <= now.AddMinutes(-2));

    internal static void Change(IntegrationDbContext context, TelegramDelivery delivery, string state, string? errorCode,
        string action, DateTimeOffset now, Guid? actorId = null)
    {
        delivery.State = state;
        delivery.ErrorCode = errorCode;
        delivery.UpdatedAtUtc = now;
        delivery.Version++;
        context.Set<TelegramDeliveryEvent>().Add(new TelegramDeliveryEvent
        {
            Id = Guid.NewGuid(),
            DeliveryId = delivery.Id,
            ActorId = actorId,
            RevisionId = delivery.RevisionId,
            Action = action,
            Attempt = delivery.Attempt,
            AtUtc = now
        });
    }

    private bool TokenConfigured => TelegramBotClient.IsValidToken(_configuration["Integrations:Telegram:BotToken"]);

    private static AccountOperationException Conflict() => new(409, "telegram_conflict", "The publication or Telegram delivery changed. Refresh before proceeding.");

    private static AccountOperationException UnavailableArticle() => new(404, "article_not_found", "The article is unavailable.");

    private async Task<AuthorizedAccountScope> BeginAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken);
        try
        {
            _context.Database.SetDbConnection(scope.Connection, contextOwnsConnection: false);
            await _context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private async Task RequireChannelAsync(string expected, CancellationToken cancellationToken)
    {
        IntegrationSettings? settings = await SettingsAsync(_context, cancellationToken);
        if (settings?.TelegramEnabled != true || Channel(settings) != expected || !TokenConfigured || IntegrationOrigin.FromConfiguration(_configuration) is null)
            throw new AccountOperationException(409, "telegram_disabled", "Telegram is disabled or the destination has changed.");
    }

    private async Task<PublicArticleAnnouncement> RequireCurrentAsync(Guid articleId, Guid revisionId, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        PublicArticleAnnouncement? article = await _articles.FindAnnouncementAsync(articleId, transaction, cancellationToken);
        if (article is null || article.RevisionId != revisionId)
            throw Conflict();
        return article;
    }

    private void ResetForQueue(TelegramDelivery delivery, Guid actorId, string action)
    {
        delivery.Attempt++;
        delivery.RateLimitRetries = 0;
        Change(_context, delivery, "Queued", null, action, _time.GetUtcNow(), actorId);
    }

    private async Task SaveEnqueuedAsync(AuthorizedAccountScope scope, TelegramDelivery delivery, CancellationToken cancellationToken)
    {
        _outbox.Enroll(_context);
        await _outbox.PublishAsync(new TelegramDeliveryMessage(delivery.Id, delivery.Attempt));
        await _context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        await _context.Database.UseTransactionAsync(null, cancellationToken);
        await _outbox.FlushOutgoingMessagesAsync();
    }

    private TelegramDeliveryResponse Describe(TelegramDelivery delivery) => new(delivery.Version, delivery.RevisionId,
        NeedsReview(delivery, _time.GetUtcNow()) ? "NeedsReview" : delivery.State, delivery.Channel, delivery.Text,
        delivery.MessageId is int id ? $"https://t.me/{delivery.Channel[1..]}/{id}" : null, delivery.ErrorCode);

    private static string ValidateText(string? value)
    {
        if (value?.Trim() is not { Length: >= 1 and <= 3000 } text || text.Contains('\0'))
            throw new AccountOperationException(400, "invalid_telegram_text", "Enter from 1 to 3000 characters without null characters.");
        return text;
    }

    private static int ReadPostId(string value, string channel)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "t.me"
            && uri.IsDefaultPort && uri.UserInfo.Length == 0)
        {
            string[] parts = uri.AbsolutePath.Split('/');
            if (parts.Length == 3 && parts[1].Equals(channel[1..], StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) && id > 0)
                return id;
        }
        throw new AccountOperationException(400, "invalid_telegram_post", "Use a public Telegram post link in the original channel.");
    }
}
