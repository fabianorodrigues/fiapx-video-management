using FiapX.VideoManagement.Application.Videos;

namespace FiapX.VideoManagement.Application.Abstractions;

public interface IVideoCache
{
    Task<IReadOnlyList<VideoResponse>?> GetVideosAsync(string userId, CancellationToken cancellationToken);
    Task<VideoResponse?> GetVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken);
    Task SetVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken);
    Task SetVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken);
    Task RemoveVideosAsync(string userId, CancellationToken cancellationToken);
    Task RemoveVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken);
}
