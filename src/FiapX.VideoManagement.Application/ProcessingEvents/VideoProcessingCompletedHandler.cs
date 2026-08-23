using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.ProcessingEvents;

public sealed class VideoProcessingCompletedHandler : ProcessingEventHandlerBase
{
    public VideoProcessingCompletedHandler(
        IVideoDataStore videos,
        IVideoCache cache,
        ILogger<VideoProcessingCompletedHandler> logger)
        : base(videos, cache, logger)
    {
    }

    public Task<ProcessingEventHandlingResult> HandleAsync(
        VideoProcessingCompleted @event,
        CancellationToken cancellationToken)
    {
        var envelope = new ProcessingEventEnvelope(
            @event.EventId,
            @event.VideoId,
            @event.UserId,
            @event.OccurredAt,
            nameof(VideoProcessingCompleted));

        return HandleAsync(envelope, video => Evaluate(video, @event), afterStateChangeAsync: null, cancellationToken);
    }

    private static ProcessingEventEvaluation Evaluate(Video video, VideoProcessingCompleted @event)
    {
        if (string.IsNullOrWhiteSpace(@event.ResultObjectKey))
        {
            return Invalid("Completed event is missing ResultObjectKey.");
        }

        var expectedResultObjectKey = VideoObjectKeys.Result(@event.UserId, @event.VideoId);
        if (!string.Equals(@event.ResultObjectKey.Trim(), expectedResultObjectKey, StringComparison.Ordinal))
        {
            return Invalid("Completed event ResultObjectKey does not match the expected video result key.");
        }

        var occurredAt = NormalizeOccurredAt(@event.OccurredAt);
        if (video.ProcessingStartedAt.HasValue && occurredAt < video.ProcessingStartedAt.Value)
        {
            return Invalid("Completed event timestamp precedes the processing start timestamp.");
        }

        return video.Status switch
        {
            VideoStatus.Recebido or VideoStatus.Processando => ApplyCompleted(video, expectedResultObjectKey, occurredAt),
            VideoStatus.Concluido when string.Equals(video.ResultObjectKey, expectedResultObjectKey, StringComparison.Ordinal) =>
                Idempotent(shouldInvalidateCache: true),
            VideoStatus.Concluido => Conflict("Completed event conflicts with the already persisted result key."),
            VideoStatus.Erro => Conflict("Completed event cannot override an error terminal status."),
            _ => Conflict("Completed event found an unsupported video status.")
        };
    }

    private static ProcessingEventEvaluation ApplyCompleted(
        Video video,
        string expectedResultObjectKey,
        DateTimeOffset occurredAt)
    {
        video.MarkCompletedFromProcessingEvent(expectedResultObjectKey, occurredAt);
        return Applied();
    }
}
