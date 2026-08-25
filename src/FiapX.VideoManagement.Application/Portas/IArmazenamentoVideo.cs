namespace FiapX.VideoManagement.Application.Portas;

public interface IArmazenamentoVideo
{
    Task<UrlPreAssinada> CriarUrlUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken);
    Task<UrlPreAssinada> CriarUrlDownloadAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed record UrlPreAssinada(string Url, int ExpiresInSeconds);
