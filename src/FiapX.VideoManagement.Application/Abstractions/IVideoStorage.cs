namespace FiapX.VideoManagement.Application.Abstractions;

public interface IVideoStorage
{
    Task<PresignedUrl> CreateUploadUrlAsync(string objectKey, string contentType, CancellationToken cancellationToken);
    Task<PresignedUrl> CreateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed record PresignedUrl(string Url, int ExpiresInSeconds);
