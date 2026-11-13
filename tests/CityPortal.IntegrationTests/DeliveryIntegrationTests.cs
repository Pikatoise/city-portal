using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CityPortal.Notifications;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quartz.Extensibility;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace CityPortal.IntegrationTests;

public sealed class DeliveryIntegrationTests : IClassFixture<DeliveryIntegrationTests.Fixture>
{
    private readonly Fixture _fixture;

    public DeliveryIntegrationTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delivery_WithStoredSmtpConfiguration_UsesPersistedServerAndBrandAcrossNewHostAsync()
    {
        using IHost host = _fixture.BuildHost(false, true);
        await host.StartAsync(CancellationToken.None);
        await using var configuredSmtp = new TestSmtp();
        int previousConnections = _fixture.Smtp.ConnectionCount;
        await _fixture.ConfigureMailAsync(host, new AccountMailOptions
        {
            SmtpHost = "127.0.0.1",
            SmtpPort = configuredSmtp.Port,
            UseTls = false,
            PortalName = "ConfiguredPortal",
            PublicOrigin = "https://configured.example.test",
            SenderAddress = "sender@example.test"
        });
        try
        {
            AccountMailRequest request = _fixture.NewRequest();
            await _fixture.QueueAsync(host, request, commit: true);
            await host.StopAsync(CancellationToken.None);
            using IHost restarted = _fixture.BuildHost(false, true);
            await restarted.StartAsync(CancellationToken.None);
            try
            {
                await _fixture.DeliverAsync(restarted, request.MessageId, 0);
                Assert.Equal("Sent", await _fixture.StateAsync(request.MessageId));
                string captured = Assert.Single(configuredSmtp.Accepted);
                using var content = new MemoryStream(Encoding.UTF8.GetBytes(captured));
                MimeKit.MimeMessage parsed = await MimeKit.MimeMessage.LoadAsync(content);
                Assert.Contains("ConfiguredPortal", parsed.Subject);
                Assert.Contains("https://configured.example.test/account/confirm-email", parsed.TextBody);
                Assert.Equal(previousConnections, _fixture.Smtp.ConnectionCount);
            }
            finally
            {
                await restarted.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await _fixture.ExecuteAsync("DELETE FROM notifications.\"MailConfiguration\"", Guid.Empty, DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task Replay_WhenSmtpRecovers_RestartsRetryBudgetKeepsAttemptNumbersAndRetainsAuditAsync()
    {
        using IHost host = _fixture.BuildHost(false, true);
        await host.StartAsync(CancellationToken.None);
        AccountMailRequest request = _fixture.NewRequest();
        await _fixture.QueueAsync(host, request, commit: true);
        _fixture.Smtp.RejectionCode = 550;
        await _fixture.DeliverAsync(host, request.MessageId, 0);
        Assert.Equal("Failed", await _fixture.StateAsync(request.MessageId));
        long version = await _fixture.ScalarAsync<long>("SELECT \"Version\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", request.MessageId);
        await _fixture.ReplayAsync(host, request.MessageId, version);
        Assert.Equal("Queued", await _fixture.StateAsync(request.MessageId));
        await _fixture.DeliverAsync(host, request.MessageId, 0);
        Assert.Equal(1L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailAttempts\" WHERE \"DeliveryId\"=@id", request.MessageId));
        _fixture.Smtp.RejectionCode = 450;
        await _fixture.DeliverAsync(host, request.MessageId, 1);
        Assert.Equal("RetryScheduled", await _fixture.StateAsync(request.MessageId));
        DateTimeOffset next = await _fixture.ScalarAsync<DateTimeOffset>("SELECT \"NextAttemptAtUtc\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", request.MessageId);
        Assert.Equal(_fixture.Time.Now.AddMinutes(1).Ticks / 10, next.Ticks / 10);
        _fixture.Time.Now = next.AddSeconds(1);
        _fixture.Smtp.RejectionCode = 0;
        await _fixture.DeliverAsync(host, request.MessageId, 2);
        Assert.Equal("Sent", await _fixture.StateAsync(request.MessageId));
        Assert.Equal(3L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailAttempts\" WHERE \"DeliveryId\"=@id", request.MessageId));
        Assert.Equal(1, _fixture.Smtp.Accepted.Count(mail => mail.Contains(request.MessageId.ToString("N"), StringComparison.Ordinal)));
        await _fixture.ExecuteAsync("UPDATE notifications.\"EmailDeliveries\" SET \"CompletedAtUtc\"=@at WHERE \"Id\"=@id", request.MessageId, _fixture.Time.Now.AddDays(-31));
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        EmailRetentionJob job = scope.ServiceProvider.GetRequiredService<EmailRetentionJob>();
        await job.Execute(JobExecutionContextBuilder.For(job).Build(), CancellationToken.None);
        Assert.Equal(0L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", request.MessageId));
        Assert.Equal(1L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"Replays\" WHERE \"DeliveryId\"=@id", request.MessageId));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RecoveryMail_WhenDelivered_UsesRecoveryPageAndRussianMultipartTemplateAsync()
    {
        using IHost host = _fixture.BuildHost(true, true);
        await host.StartAsync(CancellationToken.None);
        AccountMailRequest request = _fixture.NewRequest("recovery");
        await _fixture.QueueAsync(host, request, commit: true);
        await _fixture.DeliverAsync(host, request.MessageId, 0);
        await _fixture.WaitForStateAsync(request.MessageId, "Sent");
        string raw = Assert.Single(_fixture.Smtp.Accepted, value => value.Contains(request.MessageId.ToString("N"), StringComparison.Ordinal));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        using MimeKit.MimeMessage mail = await MimeKit.MimeMessage.LoadAsync(stream);
        Assert.Contains("восстановление пароля", mail.Subject);
        Assert.Contains("/account/reset-password#linkId=" + request.LinkId.ToString("D"), mail.TextBody);
        Assert.Contains(request.Token, mail.TextBody);
        Assert.Contains("1 час", mail.TextBody);
        Assert.Contains("Восстановить пароль", mail.HtmlBody);
        Assert.DoesNotContain("/account/confirm-email", mail.HtmlBody);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Queue_WhenTransactionRollsBack_DiscardsDeliveryAndWolverineEnvelopeAsync()
    {
        using IHost host = _fixture.BuildHost(false, false);
        await host.StartAsync(CancellationToken.None);
        AccountMailRequest request = _fixture.NewRequest();
        await _fixture.QueueAsync(host, request, commit: false);
        Assert.Equal(0L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", request.MessageId));
        Assert.Equal(0L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications_messaging.wolverine_outgoing_envelopes WHERE encode(body, 'escape') LIKE @id", $"%{request.MessageId}%"));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CommittedOutbox_WhenBrokerAndWorkerRecover_DeliversOnceAndSurvivesRestartAsync()
    {
        using IHost producer = _fixture.BuildHost(false, false);
        await producer.StartAsync(CancellationToken.None);
        ExecResult stopped = await _fixture.Broker.ExecAsync(["rabbitmqctl", "stop_app"], CancellationToken.None);
        Assert.Equal(0, stopped.ExitCode);
        AccountMailRequest request = _fixture.NewRequest();
        try
        {
            await _fixture.QueueAsync(producer, request, commit: true);
            string payload = await _fixture.ScalarAsync<string>("SELECT \"ProtectedPayload\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", request.MessageId);
            Assert.DoesNotContain(request.Token, payload, StringComparison.Ordinal);
            Assert.DoesNotContain(request.Recipient, payload, StringComparison.Ordinal);
            string body = await _fixture.ScalarAsync<string>("SELECT encode(body, 'escape') FROM notifications_messaging.wolverine_outgoing_envelopes WHERE encode(body, 'escape') LIKE @id LIMIT 1", $"%{request.MessageId}%");
            Assert.Contains(request.MessageId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(request.Token, body, StringComparison.Ordinal);
            Assert.DoesNotContain(request.Recipient, body, StringComparison.Ordinal);
            Assert.Equal("Queued", await _fixture.StateAsync(request.MessageId));
            await producer.StopAsync(CancellationToken.None);
        }
        finally
        {
            ExecResult started = await _fixture.Broker.ExecAsync(["rabbitmqctl", "start_app"], CancellationToken.None);
            Assert.Equal(0, started.ExitCode);
        }
        using IHost worker = _fixture.BuildHost(true, true);
        await worker.StartAsync(CancellationToken.None);
        await _fixture.WaitForStateAsync(request.MessageId, "Sent");
        Assert.Equal(1, _fixture.Smtp.Accepted.Count(x => x.Contains(request.MessageId.ToString("N"), StringComparison.Ordinal)));
        string mail = _fixture.Smtp.Accepted.Single(x => x.Contains(request.MessageId.ToString("N"), StringComparison.Ordinal));
        Assert.Contains("multipart/alternative", mail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/plain", mail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/html", mail, StringComparison.OrdinalIgnoreCase);

        // The initial delivery traverses RabbitMQ; await a duplicate handler invocation so a slow queue cannot hide a second send.
        await using (AsyncServiceScope duplicateScope = worker.Services.CreateAsyncScope())
            await DeliverAccountMailHandler.HandleAsync(new DeliverAccountMail { DeliveryId = request.MessageId, Attempt = 0 },
                duplicateScope.ServiceProvider.GetRequiredService<AccountMailDeliveryService>(), CancellationToken.None);
        Assert.Equal(1L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailAttempts\" WHERE \"DeliveryId\" = @id", request.MessageId));
        Assert.Equal(1, _fixture.Smtp.Accepted.Count(x => x.Contains(request.MessageId.ToString("N"), StringComparison.Ordinal)));
        Assert.Equal("", await _fixture.ScalarAsync<string>("SELECT \"ProtectedPayload\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", request.MessageId));
        await worker.StopAsync(CancellationToken.None);

        using IHost restarted = _fixture.BuildHost(true, true);
        await restarted.StartAsync(CancellationToken.None);
        await using AsyncServiceScope scope = restarted.Services.CreateAsyncScope();
        AccountMailStatus status = await scope.ServiceProvider.GetRequiredService<AccountMailQueue>().GetLatestAsync(request.MessageId, CancellationToken.None);
        Assert.Equal("Sent", status.State);
        Assert.Equal(1, _fixture.Smtp.Accepted.Count(x => x.Contains(request.MessageId.ToString("N"), StringComparison.Ordinal)));
        Assert.Equal(1L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.qrtz_job_details WHERE job_name = @id", "completed-email-retention"));
        await restarted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Delivery_WhenTransientFailureOccurs_PersistsBoundedRetriesAndThenSucceedsAsync()
    {
        using IHost host = _fixture.BuildHost(false, true);
        await host.StartAsync(CancellationToken.None);
        AccountMailRequest request = _fixture.NewRequest();
        await _fixture.QueueAsync(host, request, commit: true);
        _fixture.Smtp.RejectionCode = 451;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _fixture.Time.Now = now;
        int[] delays = [1, 5, 15, 30, 60];

        for (int attempt = 0; attempt < delays.Length; attempt++)
        {
            await _fixture.DeliverAsync(host, request.MessageId, attempt);
            Assert.Equal("RetryScheduled", await _fixture.StateAsync(request.MessageId));
            DateTimeOffset next = await _fixture.ScalarAsync<DateTimeOffset>("SELECT \"NextAttemptAtUtc\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", request.MessageId);
            Assert.Equal(_fixture.Time.Now.AddMinutes(delays[attempt]).ToUnixTimeSeconds(), next.ToUnixTimeSeconds());
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            Assert.Equal("Error", (await scope.ServiceProvider.GetRequiredService<AccountMailQueue>().GetLatestAsync(request.MessageId, CancellationToken.None)).State);
            await _fixture.DeliverAsync(host, request.MessageId, attempt + 1);
            Assert.Equal(attempt + 1, await _fixture.ScalarAsync<int>("SELECT \"AttemptCount\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", request.MessageId));
            _fixture.Time.Now = next;
        }

        _fixture.Smtp.RejectionCode = 0;
        await _fixture.DeliverAsync(host, request.MessageId, 5);
        Assert.Equal("Sent", await _fixture.StateAsync(request.MessageId));
        Assert.Equal(6L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailAttempts\" WHERE \"DeliveryId\" = @id", request.MessageId));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Delivery_WhenFailuresArePermanentOrExhausted_StopsAutomaticAttemptsAsync()
    {
        using IHost host = _fixture.BuildHost(false, true);
        await host.StartAsync(CancellationToken.None);
        _fixture.Time.Now = DateTimeOffset.UtcNow;
        AccountMailRequest permanent = _fixture.NewRequest();
        await _fixture.QueueAsync(host, permanent, commit: true);
        _fixture.Smtp.RejectionCode = 550;
        await _fixture.DeliverAsync(host, permanent.MessageId, 0);
        Assert.Equal("Failed", await _fixture.StateAsync(permanent.MessageId));
        Assert.Equal("smtp_permanent_rejection", await _fixture.ScalarAsync<string>("SELECT \"ErrorCode\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", permanent.MessageId));
        await _fixture.DeliverAsync(host, permanent.MessageId, 1);
        Assert.Equal(1, await _fixture.ScalarAsync<int>("SELECT \"AttemptCount\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", permanent.MessageId));

        AccountMailRequest exhausted = _fixture.NewRequest();
        await _fixture.QueueAsync(host, exhausted, commit: true);
        _fixture.Smtp.RejectionCode = 451;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await _fixture.DeliverAsync(host, exhausted.MessageId, attempt);
            if (attempt < 5)
                _fixture.Time.Now = await _fixture.ScalarAsync<DateTimeOffset>("SELECT \"NextAttemptAtUtc\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", exhausted.MessageId);
        }
        Assert.Equal("Failed", await _fixture.StateAsync(exhausted.MessageId));
        Assert.Equal(6, await _fixture.ScalarAsync<int>("SELECT \"AttemptCount\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", exhausted.MessageId));
        Assert.False(await _fixture.ScalarAsync<bool>("SELECT \"NextAttemptAtUtc\" IS NOT NULL FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = @id", exhausted.MessageId));
        _fixture.Smtp.RejectionCode = 0;
        await host.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delivery_WhenLinkIsExpiredOrInvalid_SkipsBeforeSmtpAsync(bool expired)
    {
        using IHost host = _fixture.BuildHost(false, true);
        await host.StartAsync(CancellationToken.None);
        _fixture.Time.Now = DateTimeOffset.UtcNow;
        AccountMailRequest request = _fixture.NewRequest();
        await _fixture.QueueAsync(host, request, commit: true);
        if (expired)
            _fixture.Time.Now = request.ExpiresAtUtc;
        else
            _fixture.Validator.InvalidLinks.Add(request.LinkId);
        int before = _fixture.Smtp.ConnectionCount;
        await _fixture.DeliverAsync(host, request.MessageId, 0);
        Assert.Equal("Skipped", await _fixture.StateAsync(request.MessageId));
        Assert.Equal(before, _fixture.Smtp.ConnectionCount);
        Assert.Equal(0L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailAttempts\" WHERE \"DeliveryId\" = @id", request.MessageId));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Retention_WhenCompletedRecordsReachThirtyDays_DeletesOnlyOlderResolvedWorkAsync()
    {
        using IHost host = _fixture.BuildHost(false, true);
        await host.StartAsync(CancellationToken.None);
        _fixture.Time.Now = DateTimeOffset.UtcNow;
        AccountMailRequest oldSent = _fixture.NewRequest();
        AccountMailRequest boundarySkipped = _fixture.NewRequest();
        AccountMailRequest unresolved = _fixture.NewRequest();
        await _fixture.QueueAsync(host, oldSent, true);
        await _fixture.QueueAsync(host, boundarySkipped, true);
        await _fixture.QueueAsync(host, unresolved, true);
        await _fixture.ExecuteAsync("UPDATE notifications.\"EmailDeliveries\" SET \"State\"='Sent', \"CompletedAtUtc\"=@at WHERE \"Id\"=@id", oldSent.MessageId, _fixture.Time.Now.AddDays(-30).AddSeconds(-1));
        await _fixture.ExecuteAsync("UPDATE notifications.\"EmailDeliveries\" SET \"State\"='Skipped', \"CompletedAtUtc\"=@at WHERE \"Id\"=@id", boundarySkipped.MessageId, _fixture.Time.Now.AddDays(-30));
        await _fixture.ExecuteAsync("UPDATE notifications.\"EmailDeliveries\" SET \"State\"='Failed', \"CompletedAtUtc\"=@at WHERE \"Id\"=@id", unresolved.MessageId, _fixture.Time.Now.AddDays(-31));
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        EmailRetentionJob job = scope.ServiceProvider.GetRequiredService<EmailRetentionJob>();
        await job.Execute(JobExecutionContextBuilder.For(job).Build(), CancellationToken.None);
        Assert.Equal(0L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", oldSent.MessageId));
        Assert.Equal("Skipped", await _fixture.StateAsync(boundarySkipped.MessageId));
        Assert.Equal("Failed", await _fixture.StateAsync(unresolved.MessageId));
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GetLatest_WhenRetainedDeliveryHasBeenCleanedUp_ProjectsErrorInsteadOfPendingAsync()
    {
        using IHost host = _fixture.BuildHost(false, true);
        await host.StartAsync(CancellationToken.None);
        _fixture.Time.Now = DateTimeOffset.UtcNow;
        AccountMailRequest request = _fixture.NewRequest();
        await _fixture.QueueAsync(host, request, commit: true);
        await _fixture.ExecuteAsync("UPDATE notifications.\"EmailDeliveries\" SET \"State\"='Sent', \"CompletedAtUtc\"=@at WHERE \"Id\"=@id", request.MessageId, _fixture.Time.Now.AddDays(-31));
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        AccountMailQueue queue = scope.ServiceProvider.GetRequiredService<AccountMailQueue>();
        Assert.Equal("Sent", (await queue.GetLatestAsync(request.MessageId, CancellationToken.None)).State);
        EmailRetentionJob job = scope.ServiceProvider.GetRequiredService<EmailRetentionJob>();
        await job.Execute(JobExecutionContextBuilder.For(job).Build(), CancellationToken.None);

        Assert.Equal(0L, await _fixture.ScalarAsync<long>("SELECT count(*) FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", request.MessageId));
        AccountMailStatus status = await queue.GetLatestAsync(request.MessageId, CancellationToken.None);
        Assert.Equal("Error", status.State);
        Assert.Null(status.LastAttemptAtUtc);
        await host.StopAsync(CancellationToken.None);
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.6-alpine3.24").Build();
        private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

        public RabbitMqContainer Broker { get; } = new RabbitMqBuilder("rabbitmq:4.4.0-management-alpine").Build();

        public TestSmtp Smtp { get; } = new();

        public TestTimeProvider Time { get; } = new();

        public TestLinkValidator Validator { get; } = new();

        public async Task InitializeAsync()
        {
            await Task.WhenAll(_database.StartAsync(CancellationToken.None), Broker.StartAsync(CancellationToken.None));
            using IHost host = BuildHost(false, false);
            await host.Services.MigrateNotificationsAsync(CancellationToken.None);
            await host.Services.MigrateAccountMailMessagingAsync();
        }

        public IHost BuildHost(bool consume, bool deliveryServices)
        {
            Time.Now = DateTimeOffset.UtcNow;
            Smtp.RejectionCode = 0;
            IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mail:SmtpHost"] = "127.0.0.1",
                ["Mail:SmtpPort"] = Smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Mail:UseTls"] = "false",
                ["Mail:PortalName"] = "РўРµСЃС‚РѕРІС‹Р№ РіРѕСЂРѕРґ"
            }).Build();
            return Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddNotificationsModule(_database.GetConnectionString(), Broker.GetConnectionString());
                    services.AddSingleton(_protection);
                    services.AddSingleton<TimeProvider>(Time);
                    services.AddSingleton<IAccountMailLinkValidator>(Validator);
                    if (deliveryServices)
                        services.AddAccountMailWorker(configuration, _database.GetConnectionString());
                }).UseAccountMailMessaging(_database.GetConnectionString(), Broker.GetConnectionString(), consume).Build();
        }

        public async Task ConfigureMailAsync(IHost host, AccountMailOptions options)
        {
            await using var connection = new NpgsqlConnection(_database.GetConnectionString());
            await connection.OpenAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MailConfigurationStore>().SaveAsync(options, connection, transaction, CancellationToken.None);
            await transaction.CommitAsync();
        }

        public AccountMailRequest NewRequest(string purpose = "confirmation") => new()
        {
            Purpose = purpose,
            MessageId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            LinkId = Guid.NewGuid(),
            Recipient = "delivery@example.test",
            Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(24)
        };

        public async Task QueueAsync(IHost host, AccountMailRequest request, bool commit)
        {
            await using var connection = new NpgsqlConnection(_database.GetConnectionString());
            await connection.OpenAsync(CancellationToken.None);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(CancellationToken.None);
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            AccountMailQueue queue = scope.ServiceProvider.GetRequiredService<AccountMailQueue>();
            await queue.QueueAsync(request, connection, transaction, CancellationToken.None);
            if (commit)
                await transaction.CommitAsync(CancellationToken.None);
            else
                await transaction.RollbackAsync(CancellationToken.None);
        }

        public async Task DeliverAsync(IHost host, Guid id, int attempt)
        {
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AccountMailDeliveryService>()
                .DeliverAsync(new DeliverAccountMail { DeliveryId = id, Attempt = attempt }, CancellationToken.None);
        }

        public async Task ReplayAsync(IHost host, Guid id, long version)
        {
            await using var connection = new NpgsqlConnection(_database.GetConnectionString());
            await connection.OpenAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            EmailAdministration administration = scope.ServiceProvider.GetRequiredService<EmailAdministration>();
            Assert.Equal(EmailReplayResult.Accepted, await administration.ReplayAsync(id, version, Guid.NewGuid(), connection, transaction, CancellationToken.None));
            await transaction.CommitAsync();
            await administration.FlushAfterCommitAsync();
        }

        public Task<string> StateAsync(Guid id) => ScalarAsync<string>("SELECT \"State\" FROM notifications.\"EmailDeliveries\" WHERE \"Id\"=@id", id);

        public async Task<T> ScalarAsync<T>(string sql, object id)
        {
            await using var connection = new NpgsqlConnection(_database.GetConnectionString());
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);
            object? value = await command.ExecuteScalarAsync(CancellationToken.None);
            if (typeof(T) == typeof(DateTimeOffset) && value is DateTime date)
                return (T)(object)new DateTimeOffset(date);
            return Assert.IsType<T>(value);
        }

        public async Task ExecuteAsync(string sql, Guid id, DateTimeOffset at)
        {
            await using var connection = new NpgsqlConnection(_database.GetConnectionString());
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("at", at);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        public async Task WaitForStateAsync(Guid id, string state)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (await StateAsync(id) != state)
                await Task.Delay(200, timeout.Token);
        }

        public async Task DisposeAsync()
        {
            await Smtp.DisposeAsync();
            await Broker.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    public sealed class TestLinkValidator : IAccountMailLinkValidator
    {
        public HashSet<Guid> InvalidLinks { get; } = [];

        public Task<bool> IsActiveAsync(Guid accountId, Guid linkId, CancellationToken cancellationToken)
            => Task.FromResult(!InvalidLinks.Contains(linkId));
    }

    public sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    public sealed class TestSmtp : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _acceptLoop;
        private int _connectionCount;

        public TestSmtp()
        {
            _listener.Start();
            _acceptLoop = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int RejectionCode { get; set; }

        public int ConnectionCount => _connectionCount;

        public ConcurrentBag<string> Accepted { get; } = [];

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _connectionCount);
                    _ = ServeAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                await using NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                try
                {
                    await writer.WriteLineAsync("220 cityportal.test SMTP");
                    while (await reader.ReadLineAsync(_stop.Token) is string line)
                    {
                        if (line.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase) && RejectionCode != 0)
                            await writer.WriteLineAsync($"{RejectionCode} test rejection");
                        else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("354 send data");
                            var body = new StringBuilder();
                            while (await reader.ReadLineAsync(_stop.Token) is string data && data != ".")
                                body.AppendLine(data);
                            Accepted.Add(body.ToString());
                            await writer.WriteLineAsync("250 accepted");
                        }
                        else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("221 bye");
                            return;
                        }
                        else
                            await writer.WriteLineAsync("250 ok");
                    }
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _acceptLoop;
            _stop.Dispose();
        }
    }
}
