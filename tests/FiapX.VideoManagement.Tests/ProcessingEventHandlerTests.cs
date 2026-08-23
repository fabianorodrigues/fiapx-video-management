using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Application.ProcessingEvents;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Tests;

public sealed class ProcessingEventHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Started_moves_received_to_processing_and_invalidates_cache()
    {
        var video = CreateVideo();
        var cache = new HandlerCache();
        var handler = CreateStartedHandler(new HandlerStore(video), cache);

        var result = await handler.HandleAsync(
            new VideoProcessingStarted(Guid.NewGuid(), video.Id, video.UserId, Now.AddMinutes(1)),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Applied, result.Outcome);
        Assert.Equal(VideoStatus.Processando, video.Status);
        Assert.Equal(Now.AddMinutes(1), video.ProcessingStartedAt);
        Assert.Contains(VideoCacheKeys.Videos(video.UserId), cache.RemovedKeys);
        Assert.Contains(VideoCacheKeys.Video(video.UserId, video.Id), cache.RemovedKeys);
    }

    [Fact]
    public async Task Started_duplicate_self_heals_cache_without_state_change()
    {
        var video = CreateVideo();
        video.MarkProcessing(Now.AddMinutes(1));
        var cache = new HandlerCache();
        var handler = CreateStartedHandler(new HandlerStore(video), cache);

        var result = await handler.HandleAsync(
            new VideoProcessingStarted(Guid.NewGuid(), video.Id, video.UserId, Now.AddMinutes(1)),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Idempotent, result.Outcome);
        Assert.False(result.StateChanged);
        Assert.Contains(VideoCacheKeys.Video(video.UserId, video.Id), cache.RemovedKeys);
    }

    [Fact]
    public async Task Completed_from_received_persists_result_without_inventing_started_at()
    {
        var video = CreateVideo();
        var handler = CreateCompletedHandler(new HandlerStore(video), new HandlerCache());

        var result = await handler.HandleAsync(
            new VideoProcessingCompleted(
                Guid.NewGuid(),
                video.Id,
                video.UserId,
                VideoObjectKeys.Result(video.UserId, video.Id),
                Now.AddMinutes(2)),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Applied, result.Outcome);
        Assert.Equal(VideoStatus.Concluido, video.Status);
        Assert.Null(video.ProcessingStartedAt);
        Assert.Equal(Now.AddMinutes(2), video.ProcessingFinishedAt);
        Assert.Equal(VideoObjectKeys.Result(video.UserId, video.Id), video.ResultObjectKey);
    }

    [Fact]
    public async Task Completed_rejects_invalid_result_key_and_incompatible_user()
    {
        var video = CreateVideo();
        var store = new HandlerStore(video);
        var handler = CreateCompletedHandler(store, new HandlerCache());

        var invalidKey = await handler.HandleAsync(
            new VideoProcessingCompleted(Guid.NewGuid(), video.Id, video.UserId, "results/other/result.zip", Now),
            CancellationToken.None);
        var wrongUser = await handler.HandleAsync(
            new VideoProcessingCompleted(Guid.NewGuid(), video.Id, "other-user", VideoObjectKeys.Result("other-user", video.Id), Now),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Invalid, invalidKey.Outcome);
        Assert.Equal(ProcessingEventHandlingOutcome.Invalid, wrongUser.Outcome);
        Assert.Equal(VideoStatus.Recebido, video.Status);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task Completed_rejects_finish_timestamp_before_existing_start()
    {
        var video = CreateVideo();
        video.MarkProcessing(Now.AddMinutes(5));
        var handler = CreateCompletedHandler(new HandlerStore(video), new HandlerCache());

        var result = await handler.HandleAsync(
            new VideoProcessingCompleted(
                Guid.NewGuid(),
                video.Id,
                video.UserId,
                VideoObjectKeys.Result(video.UserId, video.Id),
                Now.AddMinutes(4)),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Invalid, result.Outcome);
        Assert.Equal(VideoStatus.Processando, video.Status);
    }

    [Fact]
    public async Task Failed_persists_sanitized_error_invalidates_cache_and_sends_safe_email()
    {
        var video = CreateVideo();
        var cache = new HandlerCache();
        var notifications = new HandlerNotificationSender();
        var handler = CreateFailedHandler(new HandlerStore(video), cache, notifications);

        var result = await handler.HandleAsync(
            new VideoProcessingFailed(
                Guid.NewGuid(),
                video.Id,
                video.UserId,
                "processing failed",
                "boom\r\n   at Namespace.Type.Method() in C:\\internal\\worker.cs:line 9 password=secret",
                Now.AddMinutes(3)),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Applied, result.Outcome);
        Assert.Equal(VideoStatus.Erro, video.Status);
        Assert.Equal("PROCESSING_FAILED", video.ErrorCode);
        Assert.DoesNotContain("C:\\internal", video.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password=secret", video.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(notifications.Sent);
        Assert.DoesNotContain("worker.cs", notifications.Sent.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROCESSING_FAILED", notifications.Sent.Single().Message, StringComparison.Ordinal);
        Assert.Contains(VideoCacheKeys.Video(video.UserId, video.Id), cache.RemovedKeys);
    }

    [Fact]
    public async Task Failed_duplicate_self_heals_cache_without_second_email()
    {
        var video = CreateVideo();
        video.MarkFailedFromProcessingEvent("PROCESSING_FAILED", "safe", Now.AddMinutes(3));
        var cache = new HandlerCache();
        var notifications = new HandlerNotificationSender();
        var handler = CreateFailedHandler(new HandlerStore(video), cache, notifications);

        var result = await handler.HandleAsync(
            new VideoProcessingFailed(Guid.NewGuid(), video.Id, video.UserId, "PROCESSING_FAILED", "again", Now.AddMinutes(3)),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Idempotent, result.Outcome);
        Assert.Empty(notifications.Sent);
        Assert.Contains(VideoCacheKeys.Video(video.UserId, video.Id), cache.RemovedKeys);
    }

    [Fact]
    public async Task Failed_smtp_failure_keeps_error_state()
    {
        var video = CreateVideo();
        var notifications = new HandlerNotificationSender { ThrowOnSend = true };
        var handler = CreateFailedHandler(new HandlerStore(video), new HandlerCache(), notifications);

        var result = await handler.HandleAsync(
            new VideoProcessingFailed(Guid.NewGuid(), video.Id, video.UserId, "PROCESSING_FAILED", "safe", Now),
            CancellationToken.None);

        Assert.Equal(VideoStatus.Erro, video.Status);
        Assert.True(result.NotificationAttempted);
    }

    [Fact]
    public async Task Redis_failure_does_not_break_handler()
    {
        var video = CreateVideo();
        var handler = CreateStartedHandler(new HandlerStore(video), new ThrowingHandlerCache());

        var result = await handler.HandleAsync(
            new VideoProcessingStarted(Guid.NewGuid(), video.Id, video.UserId, Now),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Applied, result.Outcome);
        Assert.Equal(VideoStatus.Processando, video.Status);
        Assert.False(result.CacheInvalidated);
    }

    [Fact]
    public async Task Concurrency_conflict_reloads_and_reapplies_when_transition_is_still_valid()
    {
        var first = CreateVideo();
        var reloaded = CloneForReload(first);
        reloaded.MarkProcessing(Now.AddMinutes(1));
        var store = new HandlerStore(first)
        {
            ThrowConcurrencyOnFirstSave = true,
            ReloadedVideo = reloaded
        };
        var handler = CreateCompletedHandler(store, new HandlerCache());

        var result = await handler.HandleAsync(
            new VideoProcessingCompleted(
                Guid.NewGuid(),
                first.Id,
                first.UserId,
                VideoObjectKeys.Result(first.UserId, first.Id),
                Now.AddMinutes(2)),
            CancellationToken.None);

        Assert.Equal(ProcessingEventHandlingOutcome.Applied, result.Outcome);
        Assert.Equal(VideoStatus.Concluido, reloaded.Status);
        Assert.Equal(2, store.SaveCalls);
    }

    private static VideoProcessingStartedHandler CreateStartedHandler(IVideoDataStore store, IVideoCache cache) =>
        new(store, cache, new NullLogger<VideoProcessingStartedHandler>());

    private static VideoProcessingCompletedHandler CreateCompletedHandler(IVideoDataStore store, IVideoCache cache) =>
        new(store, cache, new NullLogger<VideoProcessingCompletedHandler>());

    private static VideoProcessingFailedHandler CreateFailedHandler(
        IVideoDataStore store,
        IVideoCache cache,
        INotificationSender notifications) =>
        new(store, cache, notifications, new NullLogger<VideoProcessingFailedHandler>());

    private static Video CreateVideo(string userId = "user-1")
    {
        var videoId = Guid.NewGuid();
        return Video.Register(
            videoId,
            userId,
            $"{userId}@fiapx.local",
            "video.mp4",
            VideoObjectKeys.Original(userId, videoId),
            VideoObjectKeys.Result(userId, videoId),
            Now);
    }

    private static Video CloneForReload(Video video) =>
        Video.Register(
            video.Id,
            video.UserId,
            video.UserEmail,
            video.OriginalFileName,
            video.OriginalObjectKey,
            VideoObjectKeys.Result(video.UserId, video.Id),
            video.CreatedAt);

    private sealed class HandlerStore : IVideoDataStore
    {
        private Video? _video;

        public HandlerStore(Video video)
        {
            _video = video;
        }

        public bool ThrowConcurrencyOnFirstSave { get; init; }
        public Video? ReloadedVideo { get; init; }
        public int SaveCalls { get; private set; }
        public int GetByIdCalls { get; private set; }

        public Task AddAsync(Video video, CancellationToken cancellationToken)
        {
            _video = video;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Video>> ListByUserAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Video>>(_video is not null && _video.UserId == userId ? [_video] : []);

        public Task<Video?> GetByUserAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult(_video is not null && _video.Id == videoId && _video.UserId == userId ? _video : null);

        public Task<Video?> GetByIdAsync(Guid videoId, CancellationToken cancellationToken)
        {
            GetByIdCalls++;
            if (GetByIdCalls > 1 && ReloadedVideo is not null)
            {
                _video = ReloadedVideo;
            }

            return Task.FromResult(_video is not null && _video.Id == videoId ? _video : null);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (ThrowConcurrencyOnFirstSave && SaveCalls == 1)
            {
                throw new VideoUpdateConcurrencyException("conflict");
            }

            return Task.CompletedTask;
        }
    }

    private class HandlerCache : IVideoCache
    {
        public List<string> RemovedKeys { get; } = [];

        public Task<IReadOnlyList<VideoResponse>?> GetVideosAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoResponse>?>(null);

        public Task<VideoResponse?> GetVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult<VideoResponse?>(null);

        public Task SetVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public virtual Task RemoveVideosAsync(string userId, CancellationToken cancellationToken)
        {
            RemovedKeys.Add(VideoCacheKeys.Videos(userId));
            return Task.CompletedTask;
        }

        public virtual Task RemoveVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken)
        {
            RemovedKeys.Add(VideoCacheKeys.Video(userId, videoId));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandlerCache : HandlerCache
    {
        public override Task RemoveVideosAsync(string userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis unavailable.");

        public override Task RemoveVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis unavailable.");
    }

    private sealed class HandlerNotificationSender : INotificationSender
    {
        public List<ProcessingFailedNotification> Sent { get; } = [];
        public bool ThrowOnSend { get; init; }

        public Task SendProcessingFailedAsync(ProcessingFailedNotification notification, CancellationToken cancellationToken)
        {
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("SMTP unavailable.");
            }

            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
