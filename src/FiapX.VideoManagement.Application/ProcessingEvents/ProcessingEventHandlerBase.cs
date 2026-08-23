using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.ProcessingEvents;

public abstract class ProcessingEventHandlerBase
{
    private readonly IVideoDataStore _videos;
    private readonly IVideoCache _cache;
    private readonly ILogger _logger;

    protected ProcessingEventHandlerBase(
        IVideoDataStore videos,
        IVideoCache cache,
        ILogger logger)
    {
        _videos = videos;
        _cache = cache;
        _logger = logger;
    }

    protected async Task<ProcessingEventHandlingResult> HandleAsync(
        ProcessingEventEnvelope envelope,
        Func<Video, ProcessingEventEvaluation> evaluate,
        Func<Video, CancellationToken, Task<bool>>? afterStateChangeAsync,
        CancellationToken cancellationToken)
    {
        if (!ValidateEnvelope(envelope))
        {
            return ProcessingEventHandlingResult(ProcessingEventHandlingOutcome.Invalid);
        }

        return await HandleAttemptAsync(envelope, evaluate, afterStateChangeAsync, allowReapply: true, cancellationToken);
    }

    private async Task<ProcessingEventHandlingResult> HandleAttemptAsync(
        ProcessingEventEnvelope envelope,
        Func<Video, ProcessingEventEvaluation> evaluate,
        Func<Video, CancellationToken, Task<bool>>? afterStateChangeAsync,
        bool allowReapply,
        CancellationToken cancellationToken)
    {
        var video = await _videos.GetByIdAsync(envelope.VideoId, cancellationToken);
        if (video is null)
        {
            LogWarning(envelope, "Processing event references an unknown video.");
            return ProcessingEventHandlingResult(ProcessingEventHandlingOutcome.NotFound);
        }

        if (!video.BelongsTo(envelope.UserId))
        {
            LogWarning(envelope, "Processing event ownership does not match the video owner.");
            return ProcessingEventHandlingResult(ProcessingEventHandlingOutcome.Invalid);
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

            return ProcessingEventHandlingResult(evaluation.Outcome, cacheInvalidated: cacheInvalidated);
        }

        try
        {
            await _videos.SaveChangesAsync(cancellationToken);
        }
        catch (VideoUpdateConcurrencyException ex) when (allowReapply)
        {
            _logger.LogWarning(
                ex,
                "Processing event hit optimistic concurrency and will be re-evaluated once. EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                envelope.EventId,
                envelope.VideoId,
                envelope.UserId,
                envelope.EventType);

            return await HandleAttemptAsync(envelope, evaluate, afterStateChangeAsync, allowReapply: false, cancellationToken);
        }
        catch (VideoUpdateConcurrencyException ex)
        {
            throw new ProcessingEventConcurrencyException(
                $"Processing event '{envelope.EventType}' could not be applied because the video changed concurrently.",
                ex);
        }

        var invalidated = await TryInvalidateCacheAsync(envelope, cancellationToken);
        var notificationAttempted = afterStateChangeAsync is not null
            && await afterStateChangeAsync(video, cancellationToken);

        return ProcessingEventHandlingResult(
            ProcessingEventHandlingOutcome.Applied,
            stateChanged: true,
            cacheInvalidated: invalidated,
            notificationAttempted: notificationAttempted);
    }

    protected static DateTimeOffset NormalizeOccurredAt(DateTimeOffset occurredAt) =>
        occurredAt.ToUniversalTime();

    protected static ProcessingEventEvaluation Invalid(string warningMessage) =>
        new(ProcessingEventHandlingOutcome.Invalid, ShouldPersist: false, ShouldInvalidateCache: false, warningMessage);

    protected static ProcessingEventEvaluation Conflict(string warningMessage) =>
        new(ProcessingEventHandlingOutcome.Conflict, ShouldPersist: false, ShouldInvalidateCache: false, warningMessage);

    protected static ProcessingEventEvaluation Idempotent(bool shouldInvalidateCache, string? warningMessage = null) =>
        new(ProcessingEventHandlingOutcome.Idempotent, ShouldPersist: false, shouldInvalidateCache, warningMessage);

    protected static ProcessingEventEvaluation Applied() =>
        new(ProcessingEventHandlingOutcome.Applied, ShouldPersist: true, ShouldInvalidateCache: true);

    private bool ValidateEnvelope(ProcessingEventEnvelope envelope)
    {
        if (envelope.EventId == Guid.Empty
            || envelope.VideoId == Guid.Empty
            || string.IsNullOrWhiteSpace(envelope.UserId)
            || envelope.OccurredAt == default)
        {
            LogWarning(envelope, "Processing event payload failed basic validation.");
            return false;
        }

        return true;
    }

    private async Task<bool> TryInvalidateCacheAsync(
        ProcessingEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var invalidated = true;

        try
        {
            await _cache.RemoveVideosAsync(envelope.UserId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            invalidated = false;
            _logger.LogWarning(
                ex,
                "Redis cache operation failed. Operation={CacheOperation} Key={CacheKey} EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                "RemoveVideos",
                VideoCacheKeys.Videos(envelope.UserId),
                envelope.EventId,
                envelope.VideoId,
                envelope.UserId,
                envelope.EventType);
        }

        try
        {
            await _cache.RemoveVideoAsync(envelope.UserId, envelope.VideoId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            invalidated = false;
            _logger.LogWarning(
                ex,
                "Redis cache operation failed. Operation={CacheOperation} Key={CacheKey} EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
                "RemoveVideo",
                VideoCacheKeys.Video(envelope.UserId, envelope.VideoId),
                envelope.EventId,
                envelope.VideoId,
                envelope.UserId,
                envelope.EventType);
        }

        return invalidated;
    }

    private void LogWarning(ProcessingEventEnvelope envelope, string message) =>
        _logger.LogWarning(
            "Processing event was not applied. Reason={Reason} EventId={EventId} VideoId={VideoId} UserId={UserId} EventType={EventType}",
            message,
            envelope.EventId,
            envelope.VideoId,
            envelope.UserId,
            envelope.EventType);

    private static ProcessingEventHandlingResult ProcessingEventHandlingResult(
        ProcessingEventHandlingOutcome outcome,
        bool stateChanged = false,
        bool cacheInvalidated = false,
        bool notificationAttempted = false) =>
        new(outcome, stateChanged, cacheInvalidated, notificationAttempted);
}

public sealed record ProcessingEventEnvelope(
    Guid EventId,
    Guid VideoId,
    string UserId,
    DateTimeOffset OccurredAt,
    string EventType);

public sealed record ProcessingEventEvaluation(
    ProcessingEventHandlingOutcome Outcome,
    bool ShouldPersist,
    bool ShouldInvalidateCache,
    string? WarningMessage = null);
