using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Application.Videos;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Tests;

public sealed class VideoServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_persists_video_and_invalidates_user_list_cache()
    {
        var store = new InMemoryVideoDataStore();
        var cache = new InMemoryVideoCache();
        var service = CreateService(store, cache);

        var response = await service.CreateAsync(new CreateVideoRequest("original.mp4", "video/mp4"), CancellationToken.None);

        Assert.Equal("RECEBIDO", response.Status);
        Assert.Single(store.Videos);
        Assert.Contains(VideoCacheKeys.Videos("user-1"), cache.RemovedKeys);
        Assert.Contains($"videos/user-1/{response.VideoId}/original.mp4", store.Videos.Single().OriginalObjectKey);
    }

    [Fact]
    public async Task List_uses_cache_hit_without_querying_postgresql()
    {
        var store = new InMemoryVideoDataStore();
        var cache = new InMemoryVideoCache();
        var cached = new[]
        {
            new VideoResponse(Guid.NewGuid(), "user-1", "cached.mp4", "key", "result", "RECEBIDO", null, Now, null, null)
        };
        cache.SetVideos("user-1", cached);
        var service = CreateService(store, cache);

        var response = await service.ListAsync(CancellationToken.None);

        Assert.Single(response);
        Assert.Equal("cached.mp4", response[0].OriginalFileName);
        Assert.Equal(0, store.ListCalls);
    }

    [Fact]
    public async Task List_cache_miss_reads_postgresql_and_populates_cache()
    {
        var store = new InMemoryVideoDataStore();
        store.Seed(CreateVideo("user-1", "db.mp4"));
        var cache = new InMemoryVideoCache();
        var service = CreateService(store, cache);

        var response = await service.ListAsync(CancellationToken.None);

        Assert.Single(response);
        Assert.Equal("db.mp4", response[0].OriginalFileName);
        Assert.Equal(1, store.ListCalls);
        Assert.True(cache.HasVideos("user-1"));
    }

    [Fact]
    public async Task Redis_unavailable_does_not_fail_request_and_logs_warning()
    {
        var store = new InMemoryVideoDataStore();
        store.Seed(CreateVideo("user-1", "db.mp4"));
        var logger = new ListLogger<VideoService>();
        var service = CreateService(store, new ThrowingVideoCache(), logger: logger);

        var response = await service.ListAsync(CancellationToken.None);

        Assert.Single(response);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("Redis cache operation failed", StringComparison.Ordinal)
            && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Get_does_not_return_video_from_another_user()
    {
        var otherUserVideo = CreateVideo("user-2", "private.mp4");
        var store = new InMemoryVideoDataStore();
        store.Seed(otherUserVideo);
        var service = CreateService(store, new InMemoryVideoCache());

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GetAsync(otherUserVideo.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Download_returns_conflict_when_video_is_not_completed()
    {
        var video = CreateVideo("user-1", "pending.mp4");
        var store = new InMemoryVideoDataStore();
        store.Seed(video);
        var service = CreateService(store, new InMemoryVideoCache());

        await Assert.ThrowsAsync<ResourceConflictException>(() =>
            service.GetDownloadAsync(video.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Download_returns_url_when_video_is_completed()
    {
        var video = CreateVideo("user-1", "completed.mp4");
        video.MarkProcessing(Now.AddMinutes(1));
        video.MarkCompleted(VideoObjectKeys.Result(video.UserId, video.Id), Now.AddMinutes(2));
        var store = new InMemoryVideoDataStore();
        store.Seed(video);
        var service = CreateService(store, new InMemoryVideoCache());

        var response = await service.GetDownloadAsync(video.Id, CancellationToken.None);

        Assert.Equal("http://localhost:9000/download", response.DownloadUrl);
    }

    private static VideoService CreateService(
        InMemoryVideoDataStore store,
        IVideoCache cache,
        ICurrentUser? currentUser = null,
        ListLogger<VideoService>? logger = null) =>
        new(
            store,
            cache,
            new StubVideoStorage(),
            currentUser ?? new StubCurrentUser("user-1", "user1@fiapx.local"),
            new FixedClock(Now),
            logger ?? new ListLogger<VideoService>());

    private static Video CreateVideo(string userId, string fileName)
    {
        var videoId = Guid.NewGuid();
        return Video.Register(
            videoId,
            userId,
            $"{userId}@fiapx.local",
            fileName,
            VideoObjectKeys.Original(userId, videoId),
            VideoObjectKeys.Result(userId, videoId),
            Now);
    }

    private sealed class InMemoryVideoDataStore : IVideoDataStore
    {
        private readonly List<Video> _videos = [];

        public IReadOnlyList<Video> Videos => _videos;
        public int ListCalls { get; private set; }

        public Task AddAsync(Video video, CancellationToken cancellationToken)
        {
            _videos.Add(video);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Video>> ListByUserAsync(string userId, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<Video>>(
                _videos.Where(video => video.UserId == userId).OrderByDescending(video => video.CreatedAt).ToArray());
        }

        public Task<Video?> GetByUserAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult(_videos.FirstOrDefault(video => video.Id == videoId && video.UserId == userId));

        public void Seed(Video video) => _videos.Add(video);
    }

    private sealed class InMemoryVideoCache : IVideoCache
    {
        private readonly Dictionary<string, IReadOnlyList<VideoResponse>> _lists = [];
        private readonly Dictionary<string, VideoResponse> _details = [];

        public List<string> RemovedKeys { get; } = [];

        public Task<IReadOnlyList<VideoResponse>?> GetVideosAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(_lists.GetValueOrDefault(VideoCacheKeys.Videos(userId)));

        public Task<VideoResponse?> GetVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult(_details.GetValueOrDefault(VideoCacheKeys.Video(userId, videoId)));

        public Task SetVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken)
        {
            SetVideos(userId, videos);
            return Task.CompletedTask;
        }

        public Task SetVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken)
        {
            _details[VideoCacheKeys.Video(userId, videoId)] = video;
            return Task.CompletedTask;
        }

        public Task RemoveVideosAsync(string userId, CancellationToken cancellationToken)
        {
            RemovedKeys.Add(VideoCacheKeys.Videos(userId));
            _lists.Remove(VideoCacheKeys.Videos(userId));
            return Task.CompletedTask;
        }

        public void SetVideos(string userId, IReadOnlyList<VideoResponse> videos) =>
            _lists[VideoCacheKeys.Videos(userId)] = videos;

        public bool HasVideos(string userId) => _lists.ContainsKey(VideoCacheKeys.Videos(userId));
    }

    private sealed class ThrowingVideoCache : IVideoCache
    {
        public Task<IReadOnlyList<VideoResponse>?> GetVideosAsync(string userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis is unavailable.");

        public Task<VideoResponse?> GetVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis is unavailable.");

        public Task SetVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis is unavailable.");

        public Task SetVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis is unavailable.");

        public Task RemoveVideosAsync(string userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Redis is unavailable.");
    }

    private sealed record StubCurrentUser(string UserId, string Email) : ICurrentUser;

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;

    private sealed class StubVideoStorage : IVideoStorage
    {
        public Task<PresignedUrl> CreateUploadUrlAsync(string objectKey, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult(new PresignedUrl("http://localhost:9000/upload", 900));

        public Task<PresignedUrl> CreateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult(new PresignedUrl("http://localhost:9000/download", 900));
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

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
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
