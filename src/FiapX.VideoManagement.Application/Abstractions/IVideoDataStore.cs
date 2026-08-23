using FiapX.VideoManagement.Domain.Videos;

namespace FiapX.VideoManagement.Application.Abstractions;

public interface IVideoDataStore
{
    Task AddAsync(Video video, CancellationToken cancellationToken);
    Task<IReadOnlyList<Video>> ListByUserAsync(string userId, CancellationToken cancellationToken);
    Task<Video?> GetByUserAsync(string userId, Guid videoId, CancellationToken cancellationToken);
}
