using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityPortal.Comments;
using CityPortal.News;
using CityPortal.Reports;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class ArticleCommentsTests : IClassFixture<RegistrationFixture>
{
    private readonly RegistrationFixture _fixture;

    public ArticleCommentsTests(RegistrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostingChecksCsrfAndLengthAndAllowsFiveAttemptsPerMinuteAsync()
    {
        var clock = new AdjustableTimeProvider();
        (ApiFactory factory, string database) = await _fixture.CreateAsync(clock);
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient visitor = await VisitorAsync(factory);
        using HttpClient withoutToken = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        ArticleResponse article = await PublishAsync(author, editor);
        string path = CommentsPath(article);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(withoutToken, path, "Missing CSRF")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(visitor, path, "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(visitor, path, new string('x', 501))).StatusCode);
        string limit = new('x', 500);
        CommentResponse first = await CreateAsync(visitor, path, " " + limit + " ");
        clock.Advance(TimeSpan.FromSeconds(1));
        CommentResponse second = await CreateAsync(visitor, path, "<b>Stored as text</b>");
        using HttpResponseMessage limited = await PostAsync(visitor, path, "Sixth attempt");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);

        CommentPage before = Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path));
        Assert.Collection(before.Items,
            item => Assert.Equal((second.Id, "<b>Stored as text</b>", false), (item.Id, item.Body, item.CanDelete)),
            item => Assert.Equal((first.Id, limit, false), (item.Id, item.Body, item.CanDelete)));
        Assert.False(before.CanModerate);
        Assert.Null(before.NextCursor);
        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        CommentResponse next = await CreateAsync(visitor, path, "New minute");
        Assert.Equal(next.Id, Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items[0].Id);
    }

    [Fact]
    public async Task CommentCursorIsStableWhenEarlierRowsAreAddedOrHiddenAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient visitor = await VisitorAsync(factory);
        ArticleResponse article = await PublishAsync(author, editor);
        string path = CommentsPath(article);
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO comments."Comments" ("Id", "ArticleId", "Body", "CreatedAtUtc")
            SELECT ('00000000-0000-0000-0000-' || lpad(n::text, 12, '0'))::uuid, @article,
                'Comment ' || n, '2026-11-25T00:00:00Z'::timestamptz
            FROM generate_series(@first, @last) n
            """, connection);
        insert.Parameters.AddWithValue("article", article.Id);
        insert.Parameters.AddWithValue("first", 1);
        insert.Parameters.AddWithValue("last", 23);
        Assert.Equal(23, await insert.ExecuteNonQueryAsync());

        CommentPage first = Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path));
        Assert.Equal(20, first.Items.Length);
        Assert.Equal("Comment 4", first.Items[^1].Body);
        string cursor = Assert.IsType<string>(first.NextCursor);
        string nextPage = path + "?cursor=" + Uri.EscapeDataString(cursor);
        insert.Parameters["first"].Value = 24;
        insert.Parameters["last"].Value = 24;
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        CommentPage afterInsert = Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(nextPage));
        Assert.Equal(["Comment 3", "Comment 2", "Comment 1"], afterInsert.Items.Select(item => item.Body));

        await using var hide = new NpgsqlCommand("""
            UPDATE comments."Comments" SET "HiddenAtUtc" = now()
            WHERE "Id" = ANY(@ids)
            """, connection);
        hide.Parameters.AddWithValue("ids", new[] { first.Items[0].Id, first.Items[1].Id, first.Items[^1].Id });
        Assert.Equal(3, await hide.ExecuteNonQueryAsync());
        CommentPage afterHide = Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(nextPage));
        Assert.Equal(afterInsert.Items, afterHide.Items);
        Assert.Null(afterHide.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.GetAsync(path + "?cursor=invalid")).StatusCode);
    }

    [Fact]
    public async Task UnconfirmedOwnerCanPostAndDeleteEvenWhileArticleIsWithdrawnAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient user = await CitizenSuggestionTests.LoginAsync(factory, database, "UnconfirmedCommenter", false);
        using HttpClient visitor = await VisitorAsync(factory);
        TaxonomyResponse category = await NewsTests.TaxonomyAsync(editor, "categories", "Comment category");
        ArticleResponse article = await NewsTests.PublishedAsync(author, editor, category.Id, "Article with comments");
        string path = CommentsPath(article);

        CommentResponse retained = await CreateAsync(user, path, "Public again on republication");
        CommentResponse owned = await CreateAsync(user, path, "Private deletion permission");
        string deletion = $"/api/account/comments/{owned.Id}";
        Assert.True(owned.CanDelete);
        Assert.Equal("UnconfirmedCommenter", owned.DisplayName);
        OwnCommentPage publishedComments = Assert.IsType<OwnCommentPage>(await user.GetFromJsonAsync<OwnCommentPage>("/api/account/comments"));
        Assert.StartsWith("/news/", article.PublicAddress);
        Assert.Equal(2, publishedComments.Items.Length);
        Assert.All(publishedComments.Items, item => Assert.Equal(article.PublicAddress, item.ArticleUrl));
        Assert.False(Assert.Single(Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items, item => item.Id == owned.Id).CanDelete);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/account/comments")).StatusCode);
        OwnCommentPage otherComments = Assert.IsType<OwnCommentPage>(await author.GetFromJsonAsync<OwnCommentPage>("/api/account/comments"));
        Assert.Empty(otherComments.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await author.DeleteAsync(deletion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.DeleteAsync(deletion)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync($"/api/staff/comments/{owned.Id}/hide", new { reason = "Not permitted" })).StatusCode);

        article = await ActAsync(editor, article, "withdraw", "Needs correction");
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(visitor, path, "Article is unavailable")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitReportAsync(visitor, retained.Id)).StatusCode);
        OwnCommentPage withdrawnComments = Assert.IsType<OwnCommentPage>(await user.GetFromJsonAsync<OwnCommentPage>("/api/account/comments"));
        Assert.True(withdrawnComments.CanDelete);
        Assert.Equal(2, withdrawnComments.Items.Length);
        Assert.All(withdrawnComments.Items, item => Assert.Null(item.ArticleUrl));
        OwnCommentResponse withdrawnComment = Assert.Single(withdrawnComments.Items, item => item.Id == owned.Id);
        Assert.Equal((owned.Id, owned.Body), (withdrawnComment.Id, withdrawnComment.Body));
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync(deletion)).StatusCode);
        OwnCommentPage remainingComments = Assert.IsType<OwnCommentPage>(await user.GetFromJsonAsync<OwnCommentPage>("/api/account/comments"));
        Assert.Equal(retained.Id, Assert.Single(remainingComments.Items).Id);

        article = Assert.IsType<ArticleResponse>(await author.GetFromJsonAsync<ArticleResponse>($"/api/editorial/articles/{article.Id}"));
        ArticleContent content = NewsDocumentTests.Content("Corrected text") with { Title = "Article republished", Summary = "Corrected summary", CategoryId = category.Id };
        article = await CitizenSuggestionTests.ReadAsync<ArticleResponse>(
            await author.PatchAsJsonAsync($"/api/editorial/articles/{article.Id}", new { article.Version, content }));
        article = await ActAsync(author, article, "submit", "Corrected");
        article = await ActAsync(editor, article, "approve");
        article = await ActAsync(author, article, "publish");
        Assert.Equal(retained.Id, Assert.Single(Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items).Id);
        OwnCommentPage republishedComments = Assert.IsType<OwnCommentPage>(await user.GetFromJsonAsync<OwnCommentPage>("/api/account/comments"));
        OwnCommentResponse republishedComment = Assert.Single(republishedComments.Items);
        Assert.Equal((retained.Id, article.PublicAddress), (republishedComment.Id, republishedComment.ArticleUrl));
    }

    [Fact]
    public async Task ReportHideRollsBackAndKeepsLaterReportsPendingAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentModerator", true, "Moderator");
        using HttpClient visitor = await VisitorAsync(factory);
        string path = CommentsPath(await PublishAsync(author, editor));
        CommentResponse comment = await CreateAsync(visitor, path, "Reported comment");
        string review = $"/api/staff/reports/comments/{comment.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitReportAsync(visitor, comment.Id)).StatusCode);
        Assert.Equal(comment.Id, Assert.Single(Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items).Id);
        ReportReviewPreview preview = Assert.IsType<ReportReviewPreview>(await moderator.GetFromJsonAsync<ReportReviewPreview>(review));
        Assert.Equal("Reported comment", preview.Text);
        Assert.True(preview.CanHide);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitReportAsync(visitor, comment.Id)).StatusCode);

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var injectFailure = new NpgsqlCommand("""
            CREATE FUNCTION reports.fail_comment_decision() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'forced test rollback';
            END;
            $$;
            CREATE TRIGGER fail_comment_decision BEFORE INSERT ON reports."Decisions"
            FOR EACH ROW EXECUTE FUNCTION reports.fail_comment_decision();
            """, connection);
        await injectFailure.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, (await DecideAsync(moderator, review, preview, "hide", "Spam")).StatusCode);
        Assert.Equal(comment.Id, Assert.Single(Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items).Id);
        Assert.Empty(Assert.IsType<ReportReviewPreview>(await moderator.GetFromJsonAsync<ReportReviewPreview>(review)).Decisions);
        await using var removeFailure = new NpgsqlCommand("""DROP TRIGGER fail_comment_decision ON reports."Decisions" """, connection);
        await removeFailure.ExecuteNonQueryAsync();

        string reason = new('s', 500);
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(moderator, review, preview, "hide", " " + reason + " ")).StatusCode);
        Assert.Empty(Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items);
        ReportReviewPreview after = Assert.IsType<ReportReviewPreview>(await moderator.GetFromJsonAsync<ReportReviewPreview>(review));
        Assert.False(after.CanHide);
        Assert.Equal(reason, Assert.Single(after.Decisions).Reason);
        Assert.Equal(1, after.Reports.Count(item => item.DecisionId is null));
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(moderator, review, after, "dismiss")).StatusCode);
        Assert.Empty(Assert.IsType<ReportQueuePage>(await moderator.GetFromJsonAsync<ReportQueuePage>("/api/staff/reports/comments")).Items);
    }

    [Fact]
    public async Task ReportPreviewBoundsPendingAndHistoricalRowsAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentModerator", true, "Moderator");
        using HttpClient visitor = await VisitorAsync(factory);
        string path = CommentsPath(await PublishAsync(author, editor));
        CommentResponse comment = await CreateAsync(visitor, path, "Reported comment");
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitReportAsync(visitor, comment.Id)).StatusCode);
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var seed = new NpgsqlCommand("""
            INSERT INTO reports."Decisions" ("Id", "ActorId", "Outcome", "DecidedAtUtc")
            SELECT md5('comment-report-decision-' || n)::uuid, gen_random_uuid(), 'dismiss', now()
            FROM generate_series(1, 23) n;
            INSERT INTO reports."Reports" ("Kind", "TargetId", "Category", "ReceivedAtUtc", "DecisionId")
            SELECT 'comments', @id, 'spam', now(), md5('comment-report-decision-' || n)::uuid
            FROM generate_series(1, 23) n;
            INSERT INTO reports."Reports" ("Kind", "TargetId", "Category", "ReceivedAtUtc")
            SELECT 'comments', @id, 'spam', now() FROM generate_series(1, 24);
            SELECT max("Sequence") FROM reports."Reports" WHERE "TargetId" = @id;
            """, connection);
        seed.Parameters.AddWithValue("id", comment.Id);
        long newest = Assert.IsType<long>(await seed.ExecuteScalarAsync());
        string review = $"/api/staff/reports/comments/{comment.Id}";
        ReportReviewPreview first = Assert.IsType<ReportReviewPreview>(await moderator.GetFromJsonAsync<ReportReviewPreview>(review));
        Assert.Equal(20, first.Reports.Count(item => item.DecisionId is null));
        Assert.Equal(20, first.Reports.Count(item => item.DecisionId is not null));
        Assert.Equal(20, first.Decisions.Length);
        Assert.True(first.HasMorePendingReports && first.HasMoreResolvedReports && first.HasMoreDecisions);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(moderator, review, first with { ThroughSequence = newest }, "dismiss")).StatusCode);
        ReportReviewPreview history = Assert.IsType<ReportReviewPreview>(
            await moderator.GetFromJsonAsync<ReportReviewPreview>(review + "?resolvedPage=2&decisionPage=2"));
        Assert.Equal(first.ThroughSequence, history.ThroughSequence);
        Assert.Equal(3, history.Reports.Count(item => item.DecisionId is not null));
        Assert.Equal(3, history.Decisions.Length);
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(moderator, review, first, "dismiss")).StatusCode);
        ReportReviewPreview after = Assert.IsType<ReportReviewPreview>(await moderator.GetFromJsonAsync<ReportReviewPreview>(review));
        Assert.Equal(5, after.Reports.Count(item => item.DecisionId is null));
    }

    [Fact]
    public async Task PasswordChangeCommitsBeforeWaitingCommentWriteRechecksSessionAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient resident = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentResident", true);
        using HttpClient visitor = await VisitorAsync(factory);
        string path = CommentsPath(await PublishAsync(author, editor));
        JsonElement profile = await resident.GetFromJsonAsync<JsonElement>("/api/account");
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var gate = new NpgsqlCommand("""
            CREATE FUNCTION comments.wait_for_password_change() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock(184502);
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER stage5_password_gate BEFORE UPDATE OF "Revoked" ON identity."Sessions"
            FOR EACH ROW EXECUTE FUNCTION comments.wait_for_password_change();
            SELECT pg_advisory_lock(184502);
            """, connection);
        await gate.ExecuteNonQueryAsync();
        Task<HttpResponseMessage> password = resident.PostAsJsonAsync("/api/account/change-password", new
        {
            currentPassword = "1234567!",
            newPassword = "Changed!123",
            version = profile.GetProperty("version").GetInt64()
        });
        Task<HttpResponseMessage> write;
        try
        {
            await WaitForAdvisoryRequestsAsync(connection, 1);
            write = PostAsync(resident, path, "Request with a revoked session");
            await WaitForAdvisoryRequestsAsync(connection, 2);
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(184502)", connection);
            await release.ExecuteNonQueryAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await password).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await write).StatusCode);
        Assert.Empty(Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items);
    }

    [Fact]
    public async Task DirectHideAppearsInHistoryAndRetainsSnapshotAfterOwnerDeletionAsync()
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient moderator = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentModerator", true, "Moderator");
        using HttpClient visitor = await VisitorAsync(factory);
        ArticleResponse article = await PublishAsync(author, editor);
        string path = CommentsPath(article);
        CommentResponse comment = await CreateAsync(moderator, path, "Moderated without a report");
        Assert.True(Assert.IsType<CommentPage>(await moderator.GetFromJsonAsync<CommentPage>(path)).CanModerate);
        string hide = $"/api/staff/comments/{comment.Id}/hide";
        Assert.Equal(HttpStatusCode.BadRequest, (await moderator.PostAsJsonAsync(hide, new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.PostAsJsonAsync(hide, new { reason = "  Personal data  " })).StatusCode);
        Assert.Empty(Assert.IsType<CommentPage>(await visitor.GetFromJsonAsync<CommentPage>(path)).Items);
        OwnCommentPage hiddenComments = Assert.IsType<OwnCommentPage>(await moderator.GetFromJsonAsync<OwnCommentPage>("/api/account/comments"));
        OwnCommentResponse hiddenComment = Assert.Single(hiddenComments.Items);
        Assert.Equal((comment.Id, article.PublicAddress), (hiddenComment.Id, hiddenComment.ArticleUrl));
        Assert.Equal(HttpStatusCode.NoContent, (await moderator.DeleteAsync($"/api/account/comments/{comment.Id}")).StatusCode);
        Assert.Equal(comment.Id, Assert.Single(Assert.IsType<ReportQueuePage>(await moderator.GetFromJsonAsync<ReportQueuePage>("/api/staff/reports/comments?history=true")).Items).Id);
        ReportReviewPreview history = Assert.IsType<ReportReviewPreview>(await moderator.GetFromJsonAsync<ReportReviewPreview>($"/api/staff/reports/comments/{comment.Id}"));
        Assert.Equal("Moderated without a report", history.Text);
        Assert.Equal("Personal data", Assert.Single(history.Decisions).Reason);
        Assert.Empty(history.Reports);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WithdrawalAndCommentWritesFollowCommitOrderAsync(bool report, bool withdrawalFirst)
    {
        (ApiFactory factory, string database) = await _fixture.CreateAsync();
        await using ApiFactory lifetime = factory;
        using HttpClient author = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentAuthor", true, "Author");
        using HttpClient editor = await CitizenSuggestionTests.LoginAsync(factory, database, "CommentEditor", true, "Editor");
        using HttpClient visitor = await VisitorAsync(factory);
        ArticleResponse article = await PublishAsync(author, editor);
        string path = CommentsPath(article);
        CommentResponse? existing = report ? await CreateAsync(visitor, path, "Available for reporting") : null;
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        string trigger = withdrawalFirst ? """BEFORE UPDATE ON news."Articles" """
            : report ? """BEFORE INSERT ON reports."Reports" """ : """BEFORE INSERT ON comments."Comments" """;
        await using var setupGate = new NpgsqlCommand($"""
            CREATE FUNCTION comments.wait_for_test_gate() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock(184501);
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER stage5_test_gate {trigger}
            FOR EACH ROW EXECUTE FUNCTION comments.wait_for_test_gate();
            SELECT pg_advisory_lock(184501);
            """, connection);
        await setupGate.ExecuteNonQueryAsync();

        Task<HttpResponseMessage> WithdrawAsync() =>
            editor.PostAsJsonAsync($"/api/editorial/articles/{article.Id}/withdraw", new { article.Version, reason = "Corrections" });
        Task<HttpResponseMessage> WriteAsync() =>
            existing is null ? PostAsync(visitor, path, "Concurrent comment") : SubmitReportAsync(visitor, existing.Id);

        Task<HttpResponseMessage> first = withdrawalFirst ? WithdrawAsync() : WriteAsync();
        Task<HttpResponseMessage> second;
        try
        {
            await WaitForAdvisoryRequestsAsync(connection, 1);
            second = withdrawalFirst ? WriteAsync() : WithdrawAsync();
            await WaitForAdvisoryRequestsAsync(connection, 2);
        }
        finally
        {
            await using var releaseGate = new NpgsqlCommand("SELECT pg_advisory_unlock(184501)", connection);
            await releaseGate.ExecuteNonQueryAsync();
        }

        using HttpResponseMessage firstResponse = await first;
        using HttpResponseMessage secondResponse = await second;
        Assert.Equal(HttpStatusCode.OK, (withdrawalFirst ? firstResponse : secondResponse).StatusCode);
        HttpStatusCode expectedWrite = withdrawalFirst ? HttpStatusCode.NotFound : report ? HttpStatusCode.NoContent : HttpStatusCode.Created;
        Assert.Equal(expectedWrite, (withdrawalFirst ? secondResponse : firstResponse).StatusCode);
        string countSql = report ? """SELECT count(*) FROM reports."Reports" WHERE "Kind" = 'comments'"""
            : """SELECT count(*) FROM comments."Comments" """;
        await using var stored = new NpgsqlCommand(countSql, connection);
        Assert.Equal(withdrawalFirst ? 0L : 1L, await stored.ExecuteScalarAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(path)).StatusCode);
    }

    private static async Task WaitForAdvisoryRequestsAsync(NpgsqlConnection connection, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event = 'advisory'
            """, connection);
        while ((long)(await command.ExecuteScalarAsync(timeout.Token) ?? 0L) < count)
            await Task.Delay(25, timeout.Token);
    }

    private static async Task<HttpClient> VisitorAsync(ApiFactory factory)
    {
        HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        JsonElement csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return client;
    }

    private static async Task<ArticleResponse> PublishAsync(HttpClient author, HttpClient editor)
    {
        TaxonomyResponse category = await NewsTests.TaxonomyAsync(editor, "categories", "Comment category");
        return await NewsTests.PublishedAsync(author, editor, category.Id, "Article with comments");
    }

    private static string CommentsPath(ArticleResponse article) => $"/api/news/articles/{article.Id}/comments";

    private static Task<ArticleResponse> ActAsync(HttpClient client, ArticleResponse article, string action, string? reason = null) =>
        ReadArticleAsync(client.PostAsJsonAsync($"/api/editorial/articles/{article.Id}/{action}", new { article.Version, reason }));

    private static async Task<ArticleResponse> ReadArticleAsync(Task<HttpResponseMessage> response) =>
        await CitizenSuggestionTests.ReadAsync<ArticleResponse>(await response);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string body) => client.PostAsJsonAsync(path, new CreateCommentRequest(body));

    private static async Task<CommentResponse> CreateAsync(HttpClient client, string path, string body)
    {
        using HttpResponseMessage response = await PostAsync(client, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<CommentResponse>(await response.Content.ReadFromJsonAsync<CommentResponse>());
    }

    private static Task<HttpResponseMessage> SubmitReportAsync(HttpClient client, Guid id) =>
        client.PostAsJsonAsync($"/api/reports/comments/{id}", new SubmitContentReportRequest { Category = "spam" });

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, string path, ReportReviewPreview preview, string outcome, string? reason = null) =>
        client.PostAsJsonAsync(path + "/decide", new DecideContentReportsRequest
        {
            Outcome = outcome,
            ThroughSequence = Assert.IsType<long>(preview.ThroughSequence),
            TargetVersion = preview.TargetVersion,
            Reason = reason
        });
}
