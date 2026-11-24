using CityPortal.CitizenSubmissions;
using CityPortal.Classifieds;
using CityPortal.Identity;
using CityPortal.Media;
using CityPortal.News;
using CityPortal.Notifications;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace CityPortal.IntegrationTests;

public sealed class RegistrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.6-alpine3.24").Build();
    private readonly RabbitMqContainer _broker = new RabbitMqBuilder("rabbitmq:4.4.0-management-alpine").Build();

    public async Task InitializeAsync() => await Task.WhenAll(_database.StartAsync(), _broker.StartAsync());

    public async Task DisposeAsync()
    {
        await _broker.DisposeAsync();
        await _database.DisposeAsync();
    }

    public async Task SetBrokerRunningAsync(bool running)
    {
        ExecResult result = await _broker.ExecAsync(["rabbitmqctl", running ? "start_app" : "stop_app"], CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }

    public async Task RestoreDatabaseAsync(string source, string target)
    {
        string backup = "/tmp/media-backup-" + Guid.NewGuid().ToString("N") + ".dump";
        string username = new NpgsqlConnectionStringBuilder(_database.GetConnectionString()).Username!;
        ExecResult dump = await _database.ExecAsync(["pg_dump", "--username", username, "--format", "custom", "--file", backup, "--dbname", source]);
        Assert.Equal(0, dump.ExitCode);
        ExecResult create = await _database.ExecAsync(["createdb", "--username", username, target]);
        Assert.Equal(0, create.ExitCode);
        ExecResult restore = await _database.ExecAsync(["pg_restore", "--username", username, "--no-owner", "--dbname", target, backup]);
        Assert.Equal(0, restore.ExitCode);
    }

    public async Task<(ApiFactory Factory, string ConnectionString)> CreateAsync(TimeProvider? time = null, bool installed = true, IReadOnlyDictionary<string, string>? settings = null)
    {
        string name = "registration_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE DATABASE " + name, connection);
        await command.ExecuteNonQueryAsync();
        // Each test uses a new database. Per-database pools would retain idle connections until the test process exits.
        var databaseSettings = new NpgsqlConnectionStringBuilder(_database.GetConnectionString()) { Database = name, Pooling = false };
        var factory = new ApiFactory("Development", databaseSettings.ConnectionString, _broker.GetConnectionString(), time, settings);
        await factory.Services.MigrateIdentityAsync(CancellationToken.None);
        await factory.Services.MigrateNotificationsAsync(CancellationToken.None);
        await factory.Services.MigrateMediaAsync(CancellationToken.None);
        await factory.Services.MigrateNewsAsync(CancellationToken.None);
        await factory.Services.MigrateCitizenSubmissionsAsync(CancellationToken.None);
        await factory.Services.MigrateClassifiedsAsync(CancellationToken.None);
        await factory.Services.MigrateAccountMailMessagingAsync();
        if (installed)
        {
            await using var siteConnection = new NpgsqlConnection(databaseSettings.ConnectionString);
            await siteConnection.OpenAsync();
            await using var site = new NpgsqlCommand("INSERT INTO identity.\"SiteConfiguration\" (\"Id\") VALUES (1)", siteConnection);
            await site.ExecuteNonQueryAsync();
        }
        return (factory, databaseSettings.ConnectionString);
    }
}
