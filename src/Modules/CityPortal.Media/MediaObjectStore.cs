using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace CityPortal.Media;

public sealed class MediaObjectStore : IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public MediaObjectStore(IOptions<MediaStorageOptions> options)
    {
        MediaStorageOptions settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
            throw new CityPortal.Identity.AccountOperationException(503, "media_unconfigured", "Media storage is not configured.");
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "https" && !(settings.AllowHttp && endpoint.Scheme == "http")
            || string.IsNullOrWhiteSpace(settings.AccessKey) || string.IsNullOrWhiteSpace(settings.SecretKey))
            throw new InvalidOperationException("Configure Media storage credentials and an HTTPS endpoint (or explicitly allow local HTTP).");
        _bucket = settings.Bucket;
        _client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), new AmazonS3Config
        {
            ServiceURL = settings.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2
        });
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = _bucket }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket, UseClientRegion = true }, cancellationToken);
        }
        // A new bucket is private by default. No public policy, ACL, or presigned URL is issued.
    }

    internal async Task PutAsync(Guid id, string variant, byte[] bytes, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(bytes, writable: false);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = Key(id, variant), InputStream = content, ContentType = "image/webp" }, cancellationToken);
    }

    internal async Task<Stream> OpenAsync(Guid id, string variant, CancellationToken cancellationToken)
    {
        GetObjectResponse response = await _client.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = Key(id, variant) }, cancellationToken);
        // MVC owns and disposes this network response stream after sending the file.
        return response.ResponseStream;
    }

    internal async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        foreach (string variant in new[] { "master", "480", "1280", "2560" })
            await _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = Key(id, variant) }, cancellationToken);
    }

    public async Task<bool> AvailableAsync(CancellationToken cancellationToken)
    {
        await _client.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = _bucket }, cancellationToken);
        return true;
    }

    private static string Key(Guid id, string variant) => $"images/{id:N}/{variant}.webp";

    public void Dispose() => _client.Dispose();
}
