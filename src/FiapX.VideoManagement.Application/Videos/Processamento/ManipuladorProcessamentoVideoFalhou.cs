using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.Videos.Processamento;

public sealed class ManipuladorProcessamentoVideoFalhou : ManipuladorEventoProcessamentoBase
{
    private readonly IEnviadorNotificacao _notifications;
    private readonly ILogger<ManipuladorProcessamentoVideoFalhou> _logger;

    public ManipuladorProcessamentoVideoFalhou(
        IRepositorioVideo videos,
        ICacheVideo cache,
        IEnviadorNotificacao notifications,
        ILogger<ManipuladorProcessamentoVideoFalhou> logger)
        : base(videos, cache, logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public Task<ResultadoTratamentoEventoProcessamento> ManipularAsync(
        VideoProcessingFailed @event,
        CancellationToken cancellationToken)
    {
        var envelope = new EnvelopeEventoProcessamento(
            @event.EventId,
            @event.VideoId,
            @event.UserId,
            @event.OccurredAt,
            nameof(VideoProcessingFailed));

        return HandleAsync(
            envelope,
            video => Evaluate(video, @event),
            (video, ct) => TrySendNotificationAsync(video, @event, ct),
            cancellationToken);
    }

    private static AvaliacaoEventoProcessamento Evaluate(Video video, VideoProcessingFailed @event)
    {
        if (string.IsNullOrWhiteSpace(@event.ErrorCode))
        {
            return Invalid("Evento de falha está sem ErrorCode.");
        }

        var normalizedErrorCode = SanitizadorEventoProcessamento.NormalizeErrorCode(@event.ErrorCode);
        if (string.IsNullOrWhiteSpace(normalizedErrorCode))
        {
            return Invalid("ErrorCode do evento de falha é inválido.");
        }

        var occurredAt = NormalizeOccurredAt(@event.OccurredAt);
        if (video.ProcessingStartedAt.HasValue && occurredAt < video.ProcessingStartedAt.Value)
        {
            return Invalid("Timestamp do evento de falha é anterior ao início do processamento.");
        }

        return video.Status switch
        {
            VideoStatus.Recebido or VideoStatus.Processando => ApplyFailed(video, normalizedErrorCode, @event.ErrorMessage, occurredAt),
            VideoStatus.Erro => Idempotent(shouldInvalidateCache: true),
            VideoStatus.Concluido => Conflict("Evento de falha não pode sobrescrever status terminal concluído."),
            _ => Conflict("Evento de falha encontrou um status de vídeo não suportado.")
        };
    }

    private static AvaliacaoEventoProcessamento ApplyFailed(
        Video video,
        string normalizedErrorCode,
        string? errorMessage,
        DateTimeOffset occurredAt)
    {
        var sanitizedErrorMessage = SanitizadorEventoProcessamento.SanitizeErrorMessage(errorMessage);
        video.MarcarErroPorEventoProcessamento(normalizedErrorCode, sanitizedErrorMessage, occurredAt);
        return Applied();
    }

    private async Task<bool> TrySendNotificationAsync(
        Video video,
        VideoProcessingFailed @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var errorCode = video.ErrorCode ?? SanitizadorEventoProcessamento.NormalizeErrorCode(@event.ErrorCode);
            await _notifications.EnviarFalhaProcessamentoAsync(
                new NotificacaoFalhaProcessamento(
                    video.UserEmail,
                    video.Id,
                    errorCode,
                    SanitizadorEventoProcessamento.ToUserSafeMessage(errorCode)),
                cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Não foi possível enviar notificação de falha do processamento. EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                @event.EventId,
                @event.VideoId,
                @event.UserId,
                nameof(VideoProcessingFailed));

            return true;
        }
    }
}
