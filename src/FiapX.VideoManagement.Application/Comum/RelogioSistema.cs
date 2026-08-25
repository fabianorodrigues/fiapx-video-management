using FiapX.VideoManagement.Application.Portas;

namespace FiapX.VideoManagement.Application.Comum;

public sealed class RelogioSistema : IRelogio
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
