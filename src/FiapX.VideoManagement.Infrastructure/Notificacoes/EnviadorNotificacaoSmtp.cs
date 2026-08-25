using System.Net.Mail;
using FiapX.VideoManagement.Application.Portas;

namespace FiapX.VideoManagement.Infrastructure.Notificacoes;

public sealed class EnviadorNotificacaoSmtp : IEnviadorNotificacao
{
    private readonly OpcoesNotificacaoSmtp _options;

    public EnviadorNotificacaoSmtp(OpcoesNotificacaoSmtp options)
    {
        _options = options;
    }

    public async Task EnviarFalhaProcessamentoAsync(
        NotificacaoFalhaProcessamento notification,
        CancellationToken cancellationToken)
    {
        using var message = new MailMessage(
            _options.From,
            notification.UserEmail,
            "FIAP X - falha no processamento do vídeo",
            $"{notification.Message}{Environment.NewLine}{Environment.NewLine}VideoId: {notification.VideoId}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            Timeout = (int)_options.Timeout.TotalMilliseconds
        };

        await client.SendMailAsync(message, timeout.Token);
    }
}
