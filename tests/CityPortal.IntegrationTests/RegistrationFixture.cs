using CityPortal.Identity;
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

    public async Task<(ApiFactory Factory, string ConnectionString)> CreateAsync(TimeProvider? time = null)
    {
        string name = "registration_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE DATABASE " + name, connection);
        await command.ExecuteNonQueryAsync();
        var settings = new NpgsqlConnectionStringBuilder(_database.GetConnectionString()) { Database = name };
        var factory = new ApiFactory("Development", settings.ConnectionString, _broker.GetConnectionString(), time);
        await factory.Services.MigrateIdentityAsync(CancellationToken.None);
        await factory.Services.MigrateNotificationsAsync(CancellationToken.None);
        await factory.Services.MigrateAccountMailMessagingAsync();
        return (factory, settings.ConnectionString);
    }
}
