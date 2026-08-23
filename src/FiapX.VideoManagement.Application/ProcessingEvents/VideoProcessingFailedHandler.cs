using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.ProcessingEvents;

public sealed class VideoProcessingFailedHandler : ProcessingEventHandlerBase
{
    private readonly INotificationSender _notifications;
    private readonly ILogger<VideoProcessingFailedHandler> _logger;

    public VideoProcessingFailedHandler(
        IVideoDataStore videos,
        IVideoCache cache,
        INotificationSender notifications,
        ILogger<VideoProcessingFailedHandler> logger)
        : base(videos, cache, logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public Task<ProcessingEventHandlingResult> HandleAsync(
        VideoProcessingFailed @event,
        CancellationToken cancellationToken)
    {
        var envelope = new ProcessingEventEnvelope(
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

    private static ProcessingEventEvaluation Evaluate(Video video, VideoProcessingFailed @event)
    {
        if (string.IsNullOrWhiteSpace(@event.ErrorCode))
        {
            return Invalid("Failed event is missing ErrorCode.");
        }

        var normalizedErrorCode = ProcessingEventSanitizer.NormalizeErrorCode(@event.ErrorCode);
        if (string.IsNullOrWhiteSpace(normalizedErrorCode))
        {
            return Invalid("Failed event ErrorCode is invalid.");
        }

        var occurredAt = NormalizeOccurredAt(@event.OccurredAt);
        if (video.ProcessingStartedAt.HasValue && occurredAt < video.ProcessingStartedAt.Value)
        {
            return Invalid("Failed event timestamp precedes the processing start timestamp.");
        }

        return video.Status switch
        {
            VideoStatus.Recebido or VideoStatus.Processando => ApplyFailed(video, normalizedErrorCode, @event.ErrorMessage, occurredAt),
            VideoStatus.Erro => Idempotent(shouldInvalidateCache: true),
            VideoStatus.Concluido => Conflict("Failed event cannot override a completed terminal status."),
            _ => Conflict("Failed event found an unsupported video status.")
        };
    }

    private static ProcessingEventEvaluation ApplyFailed(
        Video video,
        string normalizedErrorCode,
        string? errorMessage,
        DateTimeOffset occurredAt)
    {
        var sanitizedErrorMessage = ProcessingEventSanitizer.SanitizeErrorMessage(errorMessage);
        video.MarkFailedFromProcessingEvent(normalizedErrorCode, sanitizedErrorMessage, occurredAt);
        return Applied();
    }

    private async Task<bool> TrySendNotificationAsync(
        Video video,
        VideoProcessingFailed @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var errorCode = video.ErrorCode ?? ProcessingEventSanitizer.NormalizeErrorCode(@event.ErrorCode);
            await _notifications.SendProcessingFailedAsync(
                new ProcessingFailedNotification(
                    video.UserEmail,
                    video.Id,
                    errorCode,
                    ProcessingEventSanitizer.ToUserSafeMessage(errorCode)),
                cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Processing failure notification could not be sent. EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                @event.EventId,
                @event.VideoId,
                @event.UserId,
                nameof(VideoProcessingFailed));

            return true;
        }
    }
}
