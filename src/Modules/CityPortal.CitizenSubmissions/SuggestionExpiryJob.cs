using CityPortal.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Quartz;

namespace CityPortal.CitizenSubmissions;

[DisallowConcurrentExecution]
public sealed class SuggestionExpiryJob : IJob
{
    private readonly SuggestionsDbContext _context;
    private readonly TimeProvider _time;
    private readonly SuggestionImages _images;

    internal SuggestionExpiryJob(SuggestionsDbContext context, TimeProvider time, SuggestionImages images)
    {
        _context = context;
        _time = time;
        _images = images;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        Suggestion[] expired = await _context.Suggestions.FromSqlInterpolated($"""
            SELECT * FROM citizen_submissions."Suggestions"
            WHERE "PausedAtUtc" IS NULL AND "ExpiresAtUtc" <= {now}
            ORDER BY "ExpiresAtUtc", "Id" LIMIT 100 FOR UPDATE SKIP LOCKED
            """).ToArrayAsync(cancellationToken);
        foreach (Suggestion item in expired)
            await _images.ReplaceAsync(item.Id, item.ResidentId, [], _context.Database.GetDbConnection(), transaction.GetDbTransaction(), cancellationToken);
        _context.Suggestions.RemoveRange(expired);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
