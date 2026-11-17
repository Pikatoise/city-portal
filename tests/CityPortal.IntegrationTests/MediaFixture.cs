using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CityPortal.Media;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CityPortal.IntegrationTests;

public sealed class MediaFixture : IAsyncLifetime
{
    private readonly RegistrationFixture _accounts = new();
    private readonly string _access = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly string _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly IContainer _storage;

    public MediaFixture()
    {
        _storage = new ContainerBuilder("rustfs/rustfs:1.0.0@sha256:8cc9801755448b71a786705ce76692c77e14936cccd87cf2fc31842e58f4d1ff")
            .WithEnvironment("RUSTFS_ACCESS_KEY", _access).WithEnvironment("RUSTFS_SECRET_KEY", _secret)
            .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "false").WithEnvironment("RUSTFS_VOLUMES", "/data")
            .WithPortBinding(9000, true).WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9000).ForPath("/health"))).Build();
    }

    public async Task InitializeAsync() => await Task.WhenAll(_accounts.InitializeAsync(), _storage.StartAsync());

    public async Task DisposeAsync()
    {
        await _storage.DisposeAsync();
        await _accounts.DisposeAsync();
    }

    public async Task<(ApiFactory Factory, string Database, string Endpoint)> CreateAsync(IReadOnlyDictionary<string, string>? additionalSettings = null)
    {
        string endpoint = $"http://{_storage.Hostname}:{_storage.GetMappedPublicPort(9000)}";
        Dictionary<string, string> settings = new()
        {
            ["Media:Endpoint"] = endpoint,
            ["Media:AllowHttp"] = "true",
            ["Media:AccessKey"] = _access,
            ["Media:SecretKey"] = _secret,
            ["Media:Bucket"] = "media-" + Guid.NewGuid().ToString("N")
        };
        if (additionalSettings is not null)
            foreach (KeyValuePair<string, string> setting in additionalSettings)
                settings[setting.Key] = setting.Value;
        (ApiFactory factory, string database) = await _accounts.CreateAsync(settings: settings);
        await factory.Services.MigrateMediaAsync(CancellationToken.None);
        await factory.Services.GetRequiredService<MediaObjectStore>().InitializeAsync(CancellationToken.None);
        return (factory, database, endpoint);
    }

    public async Task<ApiFactory> RestoreAsync(ApiFactory source, string database, string? restoredKeysPath = null, int objectPageSize = 1000)
    {
        var settings = new NpgsqlConnectionStringBuilder(database);
        string original = settings.Database!;
        settings.Database = "media_restored_" + Guid.NewGuid().ToString("N");
        await _accounts.RestoreDatabaseAsync(original, settings.Database);
        MediaStorageOptions storage = source.Services.GetRequiredService<IOptions<MediaStorageOptions>>().Value;
        string bucket = "restored-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string> options = new()
        {
            ["Media:Endpoint"] = storage.Endpoint,
            ["Media:AllowHttp"] = "true",
            ["Media:AccessKey"] = storage.AccessKey,
            ["Media:SecretKey"] = storage.SecretKey,
            ["Media:Bucket"] = bucket
        };
        if (restoredKeysPath is not null)
            options["Security:DataProtectionKeysPath"] = restoredKeysPath;
        var restored = new ApiFactory("Development", settings.ConnectionString, source.Services.GetRequiredService<IConfiguration>().GetConnectionString("Messaging")!, settings: options);
        await restored.Services.GetRequiredService<MediaObjectStore>().InitializeAsync(CancellationToken.None);
        using var client = new AmazonS3Client(new BasicAWSCredentials(storage.AccessKey, storage.SecretKey), new AmazonS3Config { ServiceURL = storage.Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        string? continuation = null;
        do
        {
            ListObjectsV2Response objects = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = storage.Bucket, ContinuationToken = continuation, MaxKeys = objectPageSize });
            foreach (S3Object item in objects.S3Objects ?? [])
                await client.CopyObjectAsync(new CopyObjectRequest { SourceBucket = storage.Bucket, SourceKey = item.Key, DestinationBucket = bucket, DestinationKey = item.Key });
            continuation = objects.IsTruncated == true ? objects.NextContinuationToken : null;
        }
        while (continuation is not null);
        return restored;
    }
}
