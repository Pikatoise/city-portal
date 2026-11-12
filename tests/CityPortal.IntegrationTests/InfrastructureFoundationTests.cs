using System.Net;
using CityPortal.Identity;
using CityPortal.Notifications;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace CityPortal.IntegrationTests;

public sealed class InfrastructureFoundationTests
{
    [Fact]
    public async Task ModuleMigrations_OnEmptyDatabase_CreateOwnedSchemasAndPreserveDataOnRepeatAsync()
    {
        await using PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18.6-alpine3.24").Build();
        await database.StartAsync(CancellationToken.None);
        await using var factory = new ApiFactory("Production", database.GetConnectionString(), "amqp://guest:guest@127.0.0.1:1");
        using HttpClient client = factory.CreateClient();
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);

        await using var beforeMigration = new NpgsqlCommand("SELECT to_regnamespace('identity') IS NULL AND to_regnamespace('notifications') IS NULL", connection);
        Assert.Equal(true, await beforeMigration.ExecuteScalarAsync(CancellationToken.None));

        await factory.Services.MigrateIdentityAsync(CancellationToken.None);
        await factory.Services.MigrateNotificationsAsync(CancellationToken.None);

        await using var schemas = new NpgsqlCommand("SELECT to_regclass('identity.\"AspNetUsers\"') IS NOT NULL AND to_regclass('identity.\"__EFMigrationsHistory\"') IS NOT NULL AND to_regclass('notifications.\"__EFMigrationsHistory\"') IS NOT NULL", connection);
        Assert.Equal(true, await schemas.ExecuteScalarAsync(CancellationToken.None));

        var accountId = Guid.NewGuid();
        await using var insert = new NpgsqlCommand("INSERT INTO identity.\"AspNetUsers\" (\"Id\", \"DisplayName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") VALUES (@id, 'MigrationUser', 'migration@example.test', 'MIGRATION@EXAMPLE.TEST', false, NULL, NULL, NULL, false, false, false, 0)", connection);
        insert.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await insert.ExecuteNonQueryAsync(CancellationToken.None));

        await factory.Services.MigrateIdentityAsync(CancellationToken.None);
        await factory.Services.MigrateNotificationsAsync(CancellationToken.None);

        await using var read = new NpgsqlCommand("SELECT \"DisplayName\" FROM identity.\"AspNetUsers\" WHERE \"Id\" = @id", connection);
        read.Parameters.AddWithValue("id", accountId);
        Assert.Equal("MigrationUser", await read.ExecuteScalarAsync(CancellationToken.None));
        await using var histories = new NpgsqlCommand("SELECT (SELECT count(*) FROM identity.\"__EFMigrationsHistory\") + (SELECT count(*) FROM notifications.\"__EFMigrationsHistory\")", connection);
        Assert.Equal(7L, await histories.ExecuteScalarAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Readiness_WhenDependenciesFailAndRecover_TracksAvailabilityWhileLivenessRemainsHealthyAsync(bool migrateIdentityFirst)
    {
        await using PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18.6-alpine3.24").Build();
        await using RabbitMqContainer broker = new RabbitMqBuilder("rabbitmq:4.4.0-management-alpine").Build();
        await Task.WhenAll(database.StartAsync(CancellationToken.None), broker.StartAsync(CancellationToken.None));
        await using var factory = new ApiFactory("Production", database.GetConnectionString() + ";Timeout=1;Command Timeout=1", broker.GetConnectionString());
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage beforeMigration = await client.GetAsync("/health/ready", CancellationToken.None);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, beforeMigration.StatusCode);
        if (migrateIdentityFirst)
            await factory.Services.MigrateIdentityAsync(CancellationToken.None);
        else
            await factory.Services.MigrateNotificationsAsync(CancellationToken.None);

        using HttpResponseMessage partiallyMigrated = await client.GetAsync("/health/ready", CancellationToken.None);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, partiallyMigrated.StatusCode);

        if (migrateIdentityFirst)
            await factory.Services.MigrateNotificationsAsync(CancellationToken.None);
        else
            await factory.Services.MigrateIdentityAsync(CancellationToken.None);

        await factory.Services.MigrateAccountMailMessagingAsync();

        using HttpResponseMessage initialReadiness = await client.GetAsync("/health/ready", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, initialReadiness.StatusCode);

        ExecResult brokerStopped = await broker.ExecAsync(["rabbitmqctl", "stop_app"], CancellationToken.None);
        Assert.Equal(0, brokerStopped.ExitCode);

        using HttpResponseMessage unavailable = await client.GetAsync("/health/ready", CancellationToken.None);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using HttpResponseMessage liveness = await client.GetAsync("/health/live", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);

        ExecResult brokerStarted = await broker.ExecAsync(["rabbitmqctl", "start_app"], CancellationToken.None);
        Assert.Equal(0, brokerStarted.ExitCode);

        using HttpResponseMessage recovered = await client.GetAsync("/health/ready", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);

        await database.PauseAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage databaseUnavailable = await client.GetAsync("/health/ready", CancellationToken.None);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, databaseUnavailable.StatusCode);
            using HttpResponseMessage databaseOutageLiveness = await client.GetAsync("/health/live", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, databaseOutageLiveness.StatusCode);
        }
        finally
        {
            await database.UnpauseAsync(CancellationToken.None);
        }

        using HttpResponseMessage databaseRecovered = await client.GetAsync("/health/ready", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, databaseRecovered.StatusCode);
    }
}
