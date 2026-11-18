using Microsoft.EntityFrameworkCore;
using Quartz;

namespace CityPortal.CitizenSubmissions;

[DisallowConcurrentExecution]
public sealed class SuggestionExpiryJob : IJob
{
    private readonly SuggestionsDbContext _context;
    private readonly TimeProvider _time;

    internal SuggestionExpiryJob(SuggestionsDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        await _context.Suggestions.Where(item => item.ExpiresAtUtc <= now).ExecuteDeleteAsync(cancellationToken);
    }
}
