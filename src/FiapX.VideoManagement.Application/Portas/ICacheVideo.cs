using FiapX.VideoManagement.Application.Videos;

namespace FiapX.VideoManagement.Application.Portas;

public interface ICacheVideo
{
    Task<IReadOnlyList<VideoResponse>?> ObterVideosAsync(string userId, CancellationToken cancellationToken);
    Task<VideoResponse?> ObterVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken);
    Task SalvarVideosAsync(string userId, IReadOnlyList<VideoResponse> videos, CancellationToken cancellationToken);
    Task SalvarVideoAsync(string userId, Guid videoId, VideoResponse video, CancellationToken cancellationToken);
    Task RemoverVideosAsync(string userId, CancellationToken cancellationToken);
    Task RemoverVideoAsync(string userId, Guid videoId, CancellationToken cancellationToken);
}
