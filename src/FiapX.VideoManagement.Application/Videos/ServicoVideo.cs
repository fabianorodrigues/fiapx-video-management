using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Application.Comum;
using FiapX.VideoManagement.Domain.Comum;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.Videos;

public sealed class ServicoVideo
{
    private readonly IRepositorioVideo _videos;
    private readonly ICacheVideo _cache;
    private readonly IArmazenamentoVideo _storage;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRelogio _relogio;
    private readonly ILogger<ServicoVideo> _logger;

    public ServicoVideo(
        IRepositorioVideo videos,
        ICacheVideo cache,
        IArmazenamentoVideo storage,
        IUsuarioAtual usuarioAtual,
        IRelogio relogio,
        ILogger<ServicoVideo> logger)
    {
        _videos = videos;
        _cache = cache;
        _storage = storage;
        _usuarioAtual = usuarioAtual;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task<CreateVideoResponse> CriarAsync(CreateVideoRequest request, CancellationToken cancellationToken)
    {
        ValidarRequisicaoCriacao(request);

        var videoId = Guid.NewGuid();
        var userId = _usuarioAtual.UserId;
        var originalObjectKey = ChavesObjetoVideo.Original(userId, videoId);
        var resultObjectKey = ChavesObjetoVideo.Result(userId, videoId);

        var video = Video.Registrar(
            videoId,
            userId,
            _usuarioAtual.Email,
            request.FileName,
            originalObjectKey,
            resultObjectKey,
            _relogio.UtcNow);

        await _videos.AdicionarAsync(video, cancellationToken);
        await TentarRemoverVideosAsync(userId, cancellationToken);

        var uploadUrl = await _storage.CriarUrlUploadAsync(originalObjectKey, request.ContentType, cancellationToken);

        return new CreateVideoResponse(video.Id, video.Status.ParaValorContrato(), uploadUrl.Url, uploadUrl.ExpiresInSeconds);
    }

    public async Task<IReadOnlyList<VideoResponse>> ListarAsync(CancellationToken cancellationToken)
    {
        var userId = _usuarioAtual.UserId;
        var cacheKey = VideoCacheKeys.Videos(userId);
        var cached = await TentarLerCacheAsync(
            "ObterVideos",
            cacheKey,
            ct => _cache.ObterVideosAsync(userId, ct),
            cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var videos = await _videos.ListarPorUsuarioAsync(userId, cancellationToken);
        var response = videos.Select(ParaResposta).ToArray();

        await TentarEscreverCacheAsync(
            "SalvarVideos",
            cacheKey,
            ct => _cache.SalvarVideosAsync(userId, response, ct),
            cancellationToken);

        return response;
    }

    public async Task<VideoResponse> ObterAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var userId = _usuarioAtual.UserId;
        var cacheKey = VideoCacheKeys.Video(userId, videoId);
        var cached = await TentarLerCacheAsync(
            "ObterVideo",
            cacheKey,
            ct => _cache.ObterVideoAsync(userId, videoId, ct),
            cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var video = await _videos.ObterPorUsuarioAsync(userId, videoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Vídeo não encontrado.");

        var response = ParaResposta(video);

        await TentarEscreverCacheAsync(
            "SalvarVideo",
            cacheKey,
            ct => _cache.SalvarVideoAsync(userId, videoId, response, ct),
            cancellationToken);

        return response;
    }

    public async Task<DownloadVideoResponse> ObterDownloadAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var video = await _videos.ObterPorUsuarioAsync(_usuarioAtual.UserId, videoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Vídeo não encontrado.");

        try
        {
            video.GarantirDownloadDisponivel();
        }
        catch (ExcecaoDominio ex)
        {
            throw new ConflitoRecursoException(ex.Message);
        }

        var downloadUrl = await _storage.CriarUrlDownloadAsync(video.ResultObjectKey!, cancellationToken);
        return new DownloadVideoResponse(downloadUrl.Url, downloadUrl.ExpiresInSeconds);
    }

    private static VideoResponse ParaResposta(Video video) =>
        new(
            video.Id,
            video.UserId,
            video.OriginalFileName,
            video.OriginalObjectKey,
            video.ResultObjectKey,
            video.Status.ParaValorContrato(),
            video.ErrorMessage,
            video.CreatedAt,
            video.ProcessingStartedAt,
            video.ProcessingFinishedAt);

    private static void ValidarRequisicaoCriacao(CreateVideoRequest request)
    {
        if (request is null)
        {
            throw new RequisicaoInvalidaException("O corpo da requisição é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new RequisicaoInvalidaException("fileName é obrigatório.");
        }

        if (!request.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new RequisicaoInvalidaException("Somente vídeos MP4 são suportados.");
        }

        if (!string.Equals(request.ContentType, "video/mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new RequisicaoInvalidaException("contentType deve ser video/mp4.");
        }
    }

    private async Task<T?> TentarLerCacheAsync<T>(
        string operation,
        string key,
        Func<CancellationToken, Task<T?>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            return await read(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogarFalhaCache(ex, operation, key);
            return default;
        }
    }

    private async Task TentarEscreverCacheAsync(
        string operation,
        string key,
        Func<CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        try
        {
            await write(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogarFalhaCache(ex, operation, key);
        }
    }

    private Task TentarRemoverVideosAsync(string userId, CancellationToken cancellationToken) =>
        TentarEscreverCacheAsync(
            "RemoverVideos",
            VideoCacheKeys.Videos(userId),
            ct => _cache.RemoverVideosAsync(userId, ct),
            cancellationToken);

    private void LogarFalhaCache(Exception exception, string operation, string key) =>
        _logger.LogWarning(
            exception,
            "Falha na operação de cache Redis. Operation={CacheOperation} Key={CacheKey}",
            operation,
            key);
}
