namespace FiapX.VideoManagement.Application.Portas;

public interface IEnviadorNotificacao
{
    Task EnviarFalhaProcessamentoAsync(NotificacaoFalhaProcessamento notification, CancellationToken cancellationToken);
}

public sealed record NotificacaoFalhaProcessamento(
    string UserEmail,
    Guid VideoId,
    string ErrorCode,
    string Message);
