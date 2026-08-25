using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Application.Comum;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.Videos.Processamento;

public abstract class ManipuladorEventoProcessamentoBase
{
    private readonly IRepositorioVideo _videos;
    private readonly ICacheVideo _cache;
    private readonly ILogger _logger;

    protected ManipuladorEventoProcessamentoBase(
        IRepositorioVideo videos,
        ICacheVideo cache,
        ILogger logger)
    {
        _videos = videos;
        _cache = cache;
        _logger = logger;
    }

    protected async Task<ResultadoTratamentoEventoProcessamento> HandleAsync(
        EnvelopeEventoProcessamento envelope,
        Func<Video, AvaliacaoEventoProcessamento> evaluate,
        Func<Video, CancellationToken, Task<bool>>? afterStateChangeAsync,
        CancellationToken cancellationToken)
    {
        if (!ValidateEnvelope(envelope))
        {
            return ResultadoTratamentoEventoProcessamento(DesfechoTratamentoEventoProcessamento.Invalid);
        }

        return await HandleAttemptAsync(envelope, evaluate, afterStateChangeAsync, allowReapply: true, cancellationToken);
    }

    private async Task<ResultadoTratamentoEventoProcessamento> HandleAttemptAsync(
        EnvelopeEventoProcessamento envelope,
        Func<Video, AvaliacaoEventoProcessamento> evaluate,
        Func<Video, CancellationToken, Task<bool>>? afterStateChangeAsync,
        bool allowReapply,
        CancellationToken cancellationToken)
    {
        var video = await _videos.ObterPorIdAsync(envelope.VideoId, cancellationToken);
        if (video is null)
        {
            LogWarning(envelope, "Evento de processamento referencia um vídeo inexistente.");
            return ResultadoTratamentoEventoProcessamento(DesfechoTratamentoEventoProcessamento.NotFound);
        }

        if (!video.PertenceAoUsuario(envelope.UserId))
        {
            LogWarning(envelope, "Proprietário do evento de processamento não corresponde ao dono do vídeo.");
            return ResultadoTratamentoEventoProcessamento(DesfechoTratamentoEventoProcessamento.Invalid);
        }

        var evaluation = evaluate(video);
        if (!string.IsNullOrWhiteSpace(evaluation.WarningMessage))
        {
            LogWarning(envelope, evaluation.WarningMessage);
        }

        if (!evaluation.ShouldPersist)
        {
            var cacheInvalidated = evaluation.ShouldInvalidateCache
                && await TryInvalidateCacheAsync(envelope, cancellationToken);

            return ResultadoTratamentoEventoProcessamento(evaluation.Outcome, cacheInvalidated: cacheInvalidated);
        }

        try
        {
            await _videos.SalvarAlteracoesAsync(cancellationToken);
        }
        catch (ConcorrenciaAtualizacaoVideoException ex) when (allowReapply)
        {
            _logger.LogWarning(
                ex,
                "Evento de processamento encontrou concorrência otimista e será reavaliado uma vez. EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                envelope.EventId,
                envelope.VideoId,
                envelope.UserId,
                envelope.EventType);

            return await HandleAttemptAsync(envelope, evaluate, afterStateChangeAsync, allowReapply: false, cancellationToken);
        }
        catch (ConcorrenciaAtualizacaoVideoException ex)
        {
            throw new ConcorrenciaEventoProcessamentoException(
                $"Evento de processamento '{envelope.EventType}' não pôde ser aplicado porque o vídeo foi alterado concorrentemente.",
                ex);
        }

        var invalidated = await TryInvalidateCacheAsync(envelope, cancellationToken);
        var notificationAttempted = afterStateChangeAsync is not null
            && await afterStateChangeAsync(video, cancellationToken);

        return ResultadoTratamentoEventoProcessamento(
            DesfechoTratamentoEventoProcessamento.Applied,
            stateChanged: true,
            cacheInvalidated: invalidated,
            notificationAttempted: notificationAttempted);
    }

    protected static DateTimeOffset NormalizeOccurredAt(DateTimeOffset occurredAt) =>
        occurredAt.ToUniversalTime();

    protected static AvaliacaoEventoProcessamento Invalid(string warningMessage) =>
        new(DesfechoTratamentoEventoProcessamento.Invalid, ShouldPersist: false, ShouldInvalidateCache: false, warningMessage);

    protected static AvaliacaoEventoProcessamento Conflict(string warningMessage) =>
        new(DesfechoTratamentoEventoProcessamento.Conflict, ShouldPersist: false, ShouldInvalidateCache: false, warningMessage);

    protected static AvaliacaoEventoProcessamento Idempotent(bool shouldInvalidateCache, string? warningMessage = null) =>
        new(DesfechoTratamentoEventoProcessamento.Idempotent, ShouldPersist: false, shouldInvalidateCache, warningMessage);

    protected static AvaliacaoEventoProcessamento Applied() =>
        new(DesfechoTratamentoEventoProcessamento.Applied, ShouldPersist: true, ShouldInvalidateCache: true);

    private bool ValidateEnvelope(EnvelopeEventoProcessamento envelope)
    {
        if (envelope.EventId == Guid.Empty
            || envelope.VideoId == Guid.Empty
            || string.IsNullOrWhiteSpace(envelope.UserId)
            || envelope.OccurredAt == default)
        {
            LogWarning(envelope, "Payload do evento de processamento falhou na validação básica.");
            return false;
        }

        return true;
    }

    private async Task<bool> TryInvalidateCacheAsync(
        EnvelopeEventoProcessamento envelope,
        CancellationToken cancellationToken)
    {
        var invalidated = true;

        try
        {
            await _cache.RemoverVideosAsync(envelope.UserId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            invalidated = false;
            _logger.LogWarning(
                ex,
                "Falha na operação de cache Redis. Operation={CacheOperation} Key={CacheKey} EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                "RemoveVideos",
                VideoCacheKeys.Videos(envelope.UserId),
                envelope.EventId,
                envelope.VideoId,
                envelope.UserId,
                envelope.EventType);
        }

        try
        {
            await _cache.RemoverVideoAsync(envelope.UserId, envelope.VideoId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            invalidated = false;
            _logger.LogWarning(
                ex,
                "Falha na operação de cache Redis. Operation={CacheOperation} Key={CacheKey} EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                "RemoveVideo",
                VideoCacheKeys.Video(envelope.UserId, envelope.VideoId),
                envelope.EventId,
                envelope.VideoId,
                envelope.UserId,
                envelope.EventType);
        }

        return invalidated;
    }

    private void LogWarning(EnvelopeEventoProcessamento envelope, string message) =>
        _logger.LogWarning(
            "Evento de processamento não foi aplicado. Reason={Reason} EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
            message,
            envelope.EventId,
            envelope.VideoId,
            envelope.UserId,
            envelope.EventType);

    private static ResultadoTratamentoEventoProcessamento ResultadoTratamentoEventoProcessamento(
        DesfechoTratamentoEventoProcessamento outcome,
        bool stateChanged = false,
        bool cacheInvalidated = false,
        bool notificationAttempted = false) =>
        new(outcome, stateChanged, cacheInvalidated, notificationAttempted);
}

public sealed record EnvelopeEventoProcessamento(
    Guid EventId,
    Guid VideoId,
    string UserId,
    DateTimeOffset OccurredAt,
    string EventType);

public sealed record AvaliacaoEventoProcessamento(
    DesfechoTratamentoEventoProcessamento Outcome,
    bool ShouldPersist,
    bool ShouldInvalidateCache,
    string? WarningMessage = null);
