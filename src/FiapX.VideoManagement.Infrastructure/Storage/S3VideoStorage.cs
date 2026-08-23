using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FiapX.VideoManagement.Application.Abstractions;

namespace FiapX.VideoManagement.Infrastructure.Storage;

public sealed class S3VideoStorage : IVideoStorage, IDisposable
{
    private readonly S3StorageOptions _options;
    private readonly IAmazonS3 _internalClient;
    private readonly IAmazonS3 _publicPresigner;
    private readonly Protocol _publicProtocol;

    public S3VideoStorage(S3StorageOptions options)
    {
        _options = options;
        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
        _internalClient = new AmazonS3Client(credentials, CreateConfig(options.InternalEndpoint, options.Region));
        _publicPresigner = new AmazonS3Client(credentials, CreateConfig(options.PublicEndpoint, options.Region));
        _publicProtocol = new Uri(options.PublicEndpoint).Scheme == Uri.UriSchemeHttp
            ? Protocol.HTTP
            : Protocol.HTTPS;
    }

    public Task<PresignedUrl> CreateUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Protocol = _publicProtocol,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddSeconds(_options.PresignedUrlExpiresSeconds)
        };

        return Task.FromResult(new PresignedUrl(
            _publicPresigner.GetPreSignedURL(request),
            _options.PresignedUrlExpiresSeconds));
    }

    public Task<PresignedUrl> CreateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = _publicProtocol,
            Expires = DateTime.UtcNow.AddSeconds(_options.PresignedUrlExpiresSeconds)
        };

        return Task.FromResult(new PresignedUrl(
            _publicPresigner.GetPreSignedURL(request),
            _options.PresignedUrlExpiresSeconds));
    }

    public void Dispose()
    {
        _internalClient.Dispose();
        _publicPresigner.Dispose();
    }

    private static AmazonS3Config CreateConfig(string endpoint, string region)
    {
        var uri = new Uri(endpoint);

        return new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            UseHttp = uri.Scheme == Uri.UriSchemeHttp,
            AuthenticationRegion = region
        };
    }
}
