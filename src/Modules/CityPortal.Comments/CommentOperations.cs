using System.Data;
using System.Globalization;
using CityPortal.Identity;
using CityPortal.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CityPortal.Comments;

public sealed class CommentOperations
{
    private const int _pageSize = 20;
    private readonly CommentsDbContext _context;
    private readonly PublicArticleOperations _articles;
    private readonly AccountOperations _accounts;
    private readonly AccountAuthorization _authorization;
    private readonly TimeProvider _time;

    internal CommentOperations(CommentsDbContext context, PublicArticleOperations articles, AccountOperations accounts,
        AccountAuthorization authorization, TimeProvider time)
    {
        _context = context;
        _articles = articles;
        _accounts = accounts;
        _authorization = authorization;
        _time = time;
    }

    public async Task<OwnCommentPage> ListOwnAsync(Guid actorId, int page, bool canDelete, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100000)
            throw new AccountOperationException(400, "invalid_page", "Choose a valid page number.");

        var rows = await _context.Comments.AsNoTracking()
            .Where(item => item.OwnerId == actorId && item.DeletedAtUtc == null)
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((page - 1) * _pageSize).Take(_pageSize + 1)
            .Select(item => new { item.Id, item.ArticleId, item.Body, item.CreatedAtUtc })
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0)
            return new OwnCommentPage([], false, canDelete);

        var shown = rows.Take(_pageSize).ToArray();
        Guid[] articleIds = shown.Select(item => item.ArticleId).Distinct().ToArray();
        Dictionary<Guid, string> urls = await _articles.FindPublicUrlsAsync(articleIds, cancellationToken);
        OwnCommentResponse[] items = shown.Select(item => new OwnCommentResponse(
            item.Id, item.Body, item.CreatedAtUtc, urls.GetValueOrDefault(item.ArticleId))).ToArray();
        return new OwnCommentPage(items, rows.Length > _pageSize, canDelete);
    }

    public async Task<CommentPage> ListAsync(Guid articleId, string? cursor, Guid? actorId, bool canModerate, CancellationToken cancellationToken)
    {
        // Article visibility and comment rows must come from the same database snapshot.
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if ((await _articles.FindReferenceAsync(articleId, transaction.GetDbTransaction(), cancellationToken))?.PublicUrl is null)
            throw Missing();
        IQueryable<ArticleComment> visible = _context.Comments.AsNoTracking()
            .Where(item => item.ArticleId == articleId && item.DeletedAtUtc == null && item.HiddenAtUtc == null);
        if (cursor is not null)
        {
            (DateTimeOffset createdAt, Guid id) = ReadCursor(cursor);
            visible = visible.Where(item => EF.Functions.LessThan(
                ValueTuple.Create(item.CreatedAtUtc, item.Id), ValueTuple.Create(createdAt, id)));
        }
        ArticleComment[] rows = await visible
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Take(_pageSize + 1).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        string? nextCursor = rows.Length > _pageSize
            ? string.Create(CultureInfo.InvariantCulture, $"{rows[_pageSize - 1].CreatedAtUtc.UtcTicks}:{rows[_pageSize - 1].Id:N}")
            : null;
        Guid[] ownerIds = rows.Select(item => item.OwnerId).OfType<Guid>().Distinct().ToArray();
        Dictionary<Guid, string> names = await _accounts.GetPublicDisplayNamesAsync(ownerIds, cancellationToken);
        return new CommentPage(rows.Take(_pageSize)
            .Select(item => Present(item, item.OwnerId is Guid owner ? names.GetValueOrDefault(owner) : null, actorId))
            .ToArray(), nextCursor, canModerate);
    }

    public async Task<CommentResponse> CreateAsync(Guid articleId, Guid? actorId, Guid? sessionId, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        string body = request.Body?.Trim() ?? "";
        if (body.Length is < 1 or > 500 || body.Contains('\0'))
            throw new AccountOperationException(400, "invalid_comment", "Enter a comment of 1 to 500 characters.");

        if (actorId is Guid id)
        {
            await using AuthorizedAccountScope scope = await _authorization.BeginContentWriteAsync(
                id, sessionId.GetValueOrDefault(), shareRoleLock: true, cancellationToken);
            await using CommentsDbContext context = Open(scope);
            await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
            CommentResponse response = await InsertAsync(context, articleId, body, scope.Actor, cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return response;
        }

        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AccountAuthorization.LockRolesAsync(_context, shared: true, cancellationToken);
        CommentResponse anonymous = await InsertAsync(_context, articleId, body, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return anonymous;
    }

    private async Task<CommentResponse> InsertAsync(CommentsDbContext context, Guid articleId, string body, AccountResponse? actor,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Creating a comment requires a transaction.");
        if ((await _articles.FindReferenceAsync(articleId, transaction.GetDbTransaction(), cancellationToken))?.PublicUrl is null)
            throw Missing();
        var comment = new ArticleComment { ArticleId = articleId, OwnerId = actor?.Id, Body = body, CreatedAtUtc = _time.GetUtcNow() };
        context.Comments.Add(comment);
        await context.SaveChangesAsync(cancellationToken);
        return Present(comment, actor?.DisplayName, actor?.Id);
    }

    public async Task DeleteAsync(Guid id, Guid actorId, Guid sessionId, CancellationToken cancellationToken)
    {
        await using AuthorizedAccountScope scope = await _authorization.BeginContentWriteAsync(actorId, sessionId, shareRoleLock: false, cancellationToken);
        await using CommentsDbContext context = Open(scope);
        await context.Database.UseTransactionAsync(scope.Transaction, cancellationToken);
        ArticleComment comment = await context.Comments.SingleOrDefaultAsync(
            item => item.Id == id && item.OwnerId == actorId && item.DeletedAtUtc == null, cancellationToken) ?? throw Missing();
        comment.DeletedAtUtc = _time.GetUtcNow();
        comment.Body = "";
        await context.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private static CommentsDbContext Open(AuthorizedAccountScope scope) =>
        new(new DbContextOptionsBuilder<CommentsDbContext>().UseNpgsql(scope.Connection).Options);

    private static CommentResponse Present(ArticleComment comment, string? name, Guid? actorId) =>
        new(comment.Id, name ?? "Аноним", comment.Body, comment.CreatedAtUtc, actorId.HasValue && comment.OwnerId == actorId);

    private static (DateTimeOffset CreatedAt, Guid Id) ReadCursor(string cursor)
    {
        int separator = cursor.IndexOf(':');
        if (separator is < 1 or > 19 || cursor.Length != separator + 33
            || !long.TryParse(cursor.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(cursor.AsSpan(separator + 1), "N", out Guid id))
            throw new AccountOperationException(400, "invalid_comment_cursor", "Choose a valid comment cursor.");
        return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }

    private static AccountOperationException Missing() => new(404, "comment_not_found", "The article or comment is unavailable.");
}
