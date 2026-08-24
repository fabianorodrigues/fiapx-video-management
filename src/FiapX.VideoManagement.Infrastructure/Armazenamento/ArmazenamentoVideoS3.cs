using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FiapX.VideoManagement.Application.Portas;

namespace FiapX.VideoManagement.Infrastructure.Armazenamento;

public sealed class ArmazenamentoVideoS3 : IArmazenamentoVideo, IDisposable
{
    private readonly OpcoesArmazenamentoS3 _options;
    private readonly IAmazonS3 _internalClient;
    private readonly IAmazonS3 _publicPresigner;
    private readonly Protocol _publicProtocol;

    public ArmazenamentoVideoS3(OpcoesArmazenamentoS3 options)
    {
        _options = options;
        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
        _internalClient = new AmazonS3Client(credentials, CreateConfig(options.InternalEndpoint, options.Region));
        _publicPresigner = new AmazonS3Client(credentials, CreateConfig(options.PublicEndpoint, options.Region));
        _publicProtocol = new Uri(options.PublicEndpoint).Scheme == Uri.UriSchemeHttp
            ? Protocol.HTTP
            : Protocol.HTTPS;
    }

    public Task<UrlPreAssinada> CriarUrlUploadAsync(
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
            Expires = DateTime.UtcNow.AddSeconds(_options.UrlPreAssinadaExpiresSeconds)
        };

        return Task.FromResult(new UrlPreAssinada(
            _publicPresigner.GetPreSignedURL(request),
            _options.UrlPreAssinadaExpiresSeconds));
    }

    public Task<UrlPreAssinada> CriarUrlDownloadAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = _publicProtocol,
            Expires = DateTime.UtcNow.AddSeconds(_options.UrlPreAssinadaExpiresSeconds)
        };

        return Task.FromResult(new UrlPreAssinada(
            _publicPresigner.GetPreSignedURL(request),
            _options.UrlPreAssinadaExpiresSeconds));
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
