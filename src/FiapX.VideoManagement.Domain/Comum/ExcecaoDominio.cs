namespace FiapX.VideoManagement.Domain.Comum;

public sealed class ExcecaoDominio : InvalidOperationException
{
    public ExcecaoDominio(string message)
        : base(message)
    {
    }
}
