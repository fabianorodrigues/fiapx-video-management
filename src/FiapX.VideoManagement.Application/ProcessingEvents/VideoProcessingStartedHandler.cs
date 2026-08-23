using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.ProcessingEvents;

public sealed class VideoProcessingStartedHandler : ProcessingEventHandlerBase
{
    public VideoProcessingStartedHandler(
        IVideoDataStore videos,
        IVideoCache cache,
        ILogger<VideoProcessingStartedHandler> logger)
        : base(videos, cache, logger)
    {
    }

    public Task<ProcessingEventHandlingResult> HandleAsync(
        VideoProcessingStarted @event,
        CancellationToken cancellationToken)
    {
        var envelope = new ProcessingEventEnvelope(
            @event.EventId,
            @event.VideoId,
            @event.UserId,
            @event.OccurredAt,
            nameof(VideoProcessingStarted));

        return HandleAsync(envelope, video => Evaluate(video, @event), afterStateChangeAsync: null, cancellationToken);
    }

    private static ProcessingEventEvaluation Evaluate(Video video, VideoProcessingStarted @event)
    {
        var occurredAt = NormalizeOccurredAt(@event.OccurredAt);

        return video.Status switch
        {
            VideoStatus.Recebido => ApplyStarted(video, occurredAt),
            VideoStatus.Processando => Idempotent(shouldInvalidateCache: true),
            VideoStatus.Concluido or VideoStatus.Erro => Idempotent(
                shouldInvalidateCache: false,
                warningMessage: "Started event cannot regress a terminal video status."),
            _ => Conflict("Started event found an unsupported video status.")
        };
    }

    private static ProcessingEventEvaluation ApplyStarted(Video video, DateTimeOffset occurredAt)
    {
        video.MarkProcessing(occurredAt);
        return Applied();
    }
}
