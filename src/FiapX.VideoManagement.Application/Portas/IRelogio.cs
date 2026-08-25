namespace FiapX.VideoManagement.Application.Portas;

public interface IRelogio
{
    DateTimeOffset UtcNow { get; }
}
