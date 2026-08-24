using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Application.Comum;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.EntityFrameworkCore;

namespace FiapX.VideoManagement.Infrastructure.Persistencia;

public sealed class RepositorioVideoEf : IRepositorioVideo
{
    private readonly VideoDbContext _dbContext;

    public RepositorioVideoEf(VideoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AdicionarAsync(Video video, CancellationToken cancellationToken)
    {
        await _dbContext.Videos.AddAsync(video, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Video>> ListarPorUsuarioAsync(string userId, CancellationToken cancellationToken) =>
        await _dbContext.Videos
            .AsNoTracking()
            .Where(video => video.UserId == userId)
            .OrderByDescending(video => video.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Video?> ObterPorUsuarioAsync(string userId, Guid videoId, CancellationToken cancellationToken) =>
        _dbContext.Videos
            .AsNoTracking()
            .FirstOrDefaultAsync(video => video.Id == videoId && video.UserId == userId, cancellationToken);

    public Task<Video?> ObterPorIdAsync(Guid videoId, CancellationToken cancellationToken) =>
        _dbContext.Videos
            .FirstOrDefaultAsync(video => video.Id == videoId, cancellationToken);

    public async Task SalvarAlteracoesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _dbContext.ChangeTracker.Clear();
            throw new ConcorrenciaAtualizacaoVideoException("O vídeo foi alterado concorrentemente.", ex);
        }
    }
}
