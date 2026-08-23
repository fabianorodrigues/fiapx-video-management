using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Domain.Common;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.Videos;

public sealed class VideoService
{
    private readonly IVideoDataStore _videos;
    private readonly IVideoCache _cache;
    private readonly IVideoStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<VideoService> _logger;

    public VideoService(
        IVideoDataStore videos,
        IVideoCache cache,
        IVideoStorage storage,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<VideoService> logger)
    {
        _videos = videos;
        _cache = cache;
        _storage = storage;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateVideoResponse> CreateAsync(CreateVideoRequest request, CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);

        var videoId = Guid.NewGuid();
        var userId = _currentUser.UserId;
        var originalObjectKey = VideoObjectKeys.Original(userId, videoId);
        var resultObjectKey = VideoObjectKeys.Result(userId, videoId);

        var video = Video.Register(
            videoId,
            userId,
            _currentUser.Email,
            request.FileName,
            originalObjectKey,
            resultObjectKey,
            _clock.UtcNow);

        await _videos.AddAsync(video, cancellationToken);
        await TryRemoveVideosAsync(userId, cancellationToken);

        var uploadUrl = await _storage.CreateUploadUrlAsync(originalObjectKey, request.ContentType, cancellationToken);

        return new CreateVideoResponse(video.Id, video.Status.ToContractValue(), uploadUrl.Url, uploadUrl.ExpiresInSeconds);
    }

    public async Task<IReadOnlyList<VideoResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var cacheKey = VideoCacheKeys.Videos(userId);
        var cached = await TryCacheReadAsync(
            "GetVideos",
            cacheKey,
            ct => _cache.GetVideosAsync(userId, ct),
            cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var videos = await _videos.ListByUserAsync(userId, cancellationToken);
        var response = videos.Select(ToResponse).ToArray();

        await TryCacheWriteAsync(
            "SetVideos",
            cacheKey,
            ct => _cache.SetVideosAsync(userId, response, ct),
            cancellationToken);

        return response;
    }

    public async Task<VideoResponse> GetAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var cacheKey = VideoCacheKeys.Video(userId, videoId);
        var cached = await TryCacheReadAsync(
            "GetVideo",
            cacheKey,
            ct => _cache.GetVideoAsync(userId, videoId, ct),
            cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var video = await _videos.GetByUserAsync(userId, videoId, cancellationToken)
            ?? throw new ResourceNotFoundException("Video not found.");

        var response = ToResponse(video);

        await TryCacheWriteAsync(
            "SetVideo",
            cacheKey,
            ct => _cache.SetVideoAsync(userId, videoId, response, ct),
            cancellationToken);

        return response;
    }

    public async Task<DownloadVideoResponse> GetDownloadAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var video = await _videos.GetByUserAsync(_currentUser.UserId, videoId, cancellationToken)
            ?? throw new ResourceNotFoundException("Video not found.");

        try
        {
            video.EnsureCanDownload();
        }
        catch (DomainException ex)
        {
            throw new ResourceConflictException(ex.Message);
        }

        var downloadUrl = await _storage.CreateDownloadUrlAsync(video.ResultObjectKey!, cancellationToken);
        return new DownloadVideoResponse(downloadUrl.Url, downloadUrl.ExpiresInSeconds);
    }

    private static VideoResponse ToResponse(Video video) =>
        new(
            video.Id,
            video.UserId,
            video.OriginalFileName,
            video.OriginalObjectKey,
            video.ResultObjectKey,
            video.Status.ToContractValue(),
            video.ErrorMessage,
            video.CreatedAt,
            video.ProcessingStartedAt,
            video.ProcessingFinishedAt);

    private static void ValidateCreateRequest(CreateVideoRequest request)
    {
        if (request is null)
        {
            throw new RequestValidationException("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new RequestValidationException("fileName is required.");
        }

        if (!request.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("Only MP4 videos are supported.");
        }

        if (!string.Equals(request.ContentType, "video/mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("contentType must be video/mp4.");
        }
    }

    private async Task<T?> TryCacheReadAsync<T>(
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
            LogCacheWarning(ex, operation, key);
            return default;
        }
    }

    private async Task TryCacheWriteAsync(
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
            LogCacheWarning(ex, operation, key);
        }
    }

    private Task TryRemoveVideosAsync(string userId, CancellationToken cancellationToken) =>
        TryCacheWriteAsync(
            "RemoveVideos",
            VideoCacheKeys.Videos(userId),
            ct => _cache.RemoveVideosAsync(userId, ct),
            cancellationToken);

    private void LogCacheWarning(Exception exception, string operation, string key) =>
        _logger.LogWarning(
            exception,
            "Redis cache operation failed. Operation={CacheOperation} Key={CacheKey}",
            operation,
            key);
}
