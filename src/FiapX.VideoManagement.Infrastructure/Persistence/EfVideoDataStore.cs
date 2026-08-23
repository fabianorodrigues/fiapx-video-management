using FiapX.VideoManagement.Application.Abstractions;
using FiapX.VideoManagement.Application.Common;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.EntityFrameworkCore;

namespace FiapX.VideoManagement.Infrastructure.Persistence;

public sealed class EfVideoDataStore : IVideoDataStore
{
    private readonly VideoDbContext _dbContext;

    public EfVideoDataStore(VideoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Video video, CancellationToken cancellationToken)
    {
        await _dbContext.Videos.AddAsync(video, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Video>> ListByUserAsync(string userId, CancellationToken cancellationToken) =>
        await _dbContext.Videos
            .AsNoTracking()
            .Where(video => video.UserId == userId)
            .OrderByDescending(video => video.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Video?> GetByUserAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
        _dbContext.Videos
            .AsNoTracking()
            .FirstOrDefaultAsync(video => video.Id == videoId && video.UserId == userId, cancellationToken);

    public Task<Video?> GetByIdAsync(Guid videoId, CancellationToken cancellationToken) =>
        _dbContext.Videos
            .FirstOrDefaultAsync(video => video.Id == videoId, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _dbContext.ChangeTracker.Clear();
            throw new VideoUpdateConcurrencyException("Video was changed concurrently.", ex);
        }
    }
}
