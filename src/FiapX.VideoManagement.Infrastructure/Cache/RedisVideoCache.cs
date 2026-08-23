using System.Text.Json;
using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Videos;
using StackExchange.Redis;

namespace FiapX.VideoManagement.Infrastructure.Cache;

public sealed class RedisVideoCache : IVideoCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lazy<IConnectionMultiplexer> _connection;
    private readonly TimeSpan _ttl;

    public RedisVideoCache(string connectionString, TimeSpan ttl)
    {
        _ttl = ttl;
        _connection = new Lazy<IConnectionMultiplexer>(() =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
    }

    public Task<IReadOnlyList<VideoResponse>?> GetVideosAsync(string userId, CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<VideoResponse>>(VideoCacheKeys.Videos(userId), cancellationToken);

    public Task<VideoResponse?> GetVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
        GetAsync<VideoResponse>(VideoCacheKeys.Video(userId, videoId), cancellationToken);

    public Task SetVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken) =>
        SetAsync(VideoCacheKeys.Videos(userId), videos, cancellationToken);

    public Task SetVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken) =>
        SetAsync(VideoCacheKeys.Video(userId, videoId), video, cancellationToken);

    public async Task RemoveVideosAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Database.KeyDeleteAsync(VideoCacheKeys.Videos(userId));
    }

    public async Task RemoveVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Database.KeyDeleteAsync(VideoCacheKeys.Video(userId, videoId));
    }

    private IDatabase Database => _connection.Value.GetDatabase();

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cached = await Database.StringGetAsync(key);

        if (cached.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>((string)cached!, JsonOptions);
    }

    private async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await Database.StringSetAsync(key, json, _ttl);
    }
}
