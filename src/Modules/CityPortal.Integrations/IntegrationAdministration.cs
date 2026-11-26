using System.Text.Json;
using CityPortal.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CityPortal.Integrations;

public sealed class IntegrationAdministration
{
    private static readonly string[] _roles = ["Administrator"];
    private readonly IntegrationDbContext _context;
    private readonly AccountAuthorization _authorization;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    public IntegrationAdministration(IntegrationDbContext context, AccountAuthorization authorization, IConfiguration configuration, TimeProvider time)
    {
        _context = context;
        _authorization = authorization;
        _configuration = configuration;
        _time = time;
    }

    public async Task<IntegrationStatus> GetAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        await _authorization.RequireAsync(actorId, sessionId, _roles, cancellationToken);
        IntegrationSettings settings = await _context.Settings.AsNoTracking().SingleAsync(cancellationToken);
        return await ReadAsync(_context, settings, cancellationToken);
    }

    public async Task<IntegrationStatus> SaveAsync(Guid actorId, Guid sessionId, SaveIntegrationSettings request, CancellationToken cancellationToken)
    {
        await using (AuthorizedAccountScope scope = await _authorization.BeginAsync(actorId, sessionId, _roles, cancellationToken))
        {
            await using var context = new IntegrationDbContext(new DbContextOptionsBuilder<IntegrationDbContext>().UseNpgsql(scope.Connection).Options);
            await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
            await context.Database.ExecuteSqlRawAsync("""SELECT 1 FROM integrations.settings WHERE "Id" = 1 FOR UPDATE""", cancellationToken);
            IntegrationSettings settings = await context.Settings.SingleAsync(cancellationToken);
            if (settings.Version != request.Version)
                throw new AccountOperationException(409, "version_conflict", "Review the latest integration settings before saving.");

            string? channel = Validate(request);
            if (!request.WeatherEnabled || request.Latitude != settings.Latitude || request.Longitude != settings.Longitude)
                await context.WeatherSnapshots.ExecuteDeleteAsync(cancellationToken);

            settings.Version++;
            settings.RssEnabled = request.RssEnabled;
            settings.WeatherEnabled = request.WeatherEnabled;
            settings.Latitude = request.Latitude;
            settings.Longitude = request.Longitude;
            settings.TelegramEnabled = request.TelegramEnabled;
            settings.TelegramChannel = channel;
            settings.AiEnabled = request.AiEnabled;
            settings.McpEnabled = request.McpEnabled;
            context.SettingsChanges.Add(new IntegrationSettingsChange
            {
                ActorId = actorId,
                Version = settings.Version,
                ChangedAtUtc = _time.GetUtcNow(),
                Settings = JsonSerializer.Serialize(ToResponse(settings))
            });
            await context.SaveChangesAsync(cancellationToken);
            IntegrationStatus response = await ReadAsync(context, settings, cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return response;
        }
    }

    private async Task<IntegrationStatus> ReadAsync(IntegrationDbContext context, IntegrationSettings settings, CancellationToken cancellationToken)
    {
        DateTimeOffset? refreshedAt = await context.WeatherSnapshots.AsNoTracking()
            .Where(snapshot => settings.WeatherEnabled && snapshot.Latitude == settings.Latitude && snapshot.Longitude == settings.Longitude)
            .Select(snapshot => (DateTimeOffset?)snapshot.RefreshedAtUtc).SingleOrDefaultAsync(cancellationToken);
        (int pending, int needsAttention) = await GetDeliveryCountsAsync(context, cancellationToken);
        return new IntegrationStatus(ToResponse(settings),
            TelegramBotClient.IsValidToken(_configuration["Integrations:Telegram:BotToken"]),
            !string.IsNullOrWhiteSpace(_configuration["Integrations:OpenAI:ApiKey"]),
            IntegrationOrigin.ParseHttps(_configuration["Mcp:Authority"]) is not null
                && IntegrationOrigin.FromConfiguration(_configuration) is not null
                && _configuration.GetSection("Mcp:AllowedClientIds").GetChildren().Any(item => !string.IsNullOrWhiteSpace(item.Value)),
            refreshedAt, pending, needsAttention);
    }

    private static SaveIntegrationSettings ToResponse(IntegrationSettings settings) =>
        new(settings.Version, settings.RssEnabled, settings.WeatherEnabled, settings.Latitude, settings.Longitude,
            settings.TelegramEnabled, settings.TelegramChannel, settings.AiEnabled, settings.McpEnabled);

    private async Task<(int Pending, int NeedsAttention)> GetDeliveryCountsAsync(IntegrationDbContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset sendingCutoff = _time.GetUtcNow().AddMinutes(-2);
        var counts = await context.Set<TelegramDelivery>().AsNoTracking()
            .Where(item => item.State == "Queued" || item.State == "RetryScheduled" || item.State == "Sending"
                || item.State == "Failed" || item.State == "NeedsReview").GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(item => item.State == "Queued" || item.State == "RetryScheduled"
                    || item.State == "Sending" && item.UpdatedAtUtc > sendingCutoff),
                NeedsAttention = group.Count(item => item.State == "Failed" || item.State == "NeedsReview"
                    || item.State == "Sending" && item.UpdatedAtUtc <= sendingCutoff)
            }).SingleOrDefaultAsync(cancellationToken);
        return (counts?.Pending ?? 0, counts?.NeedsAttention ?? 0);
    }

    private static string? Validate(SaveIntegrationSettings settings)
    {
        if (settings.Latitude.HasValue != settings.Longitude.HasValue || settings.WeatherEnabled && settings.Latitude is null
            || settings.Latitude is double latitude && (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            || settings.Longitude is double longitude && (!double.IsFinite(longitude) || longitude is < -180 or > 180))
            throw Invalid("Enter valid latitude and longitude together.");

        string? channel = settings.TelegramChannel?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(channel))
        {
            if (settings.TelegramEnabled)
                throw Invalid("Enter a public Telegram channel handle.");
            return null;
        }

        if (channel.Length is < 6 or > 33 || channel[0] != '@' || !char.IsAsciiLetter(channel[1])
            || channel.Skip(2).Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw Invalid("Enter a public Telegram channel handle beginning with @.");

        return channel;
    }

    private static AccountOperationException Invalid(string message) => new(400, "validation_failed", message);
}
