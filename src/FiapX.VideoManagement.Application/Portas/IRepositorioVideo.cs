using FiapX.VideoManagement.Domain.Videos;

namespace FiapX.VideoManagement.Application.Portas;

public interface IRepositorioVideo
{
    Task AdicionarAsync(Video video, CancellationToken cancellationToken);
    Task<IReadOnlyList<Video>> ListarPorUsuarioAsync(string userId, CancellationToken cancellationToken);
    Task<Video?> ObterPorUsuarioAsync(string userId, Guid videoId, CancellationToken cancellationToken);
    Task<Video?> ObterPorIdAsync(Guid videoId, CancellationToken cancellationToken);
    Task SalvarAlteracoesAsync(CancellationToken cancellationToken);
}
