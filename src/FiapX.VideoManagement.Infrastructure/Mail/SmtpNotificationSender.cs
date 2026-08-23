using System.Net.Mail;
using FiapX.VideoManagement.Application.Abstractions;

namespace FiapX.VideoManagement.Infrastructure.Mail;

public sealed class SmtpNotificationSender : INotificationSender
{
    private readonly SmtpNotificationOptions _options;

    public SmtpNotificationSender(SmtpNotificationOptions options)
    {
        _options = options;
    }

    public async Task SendProcessingFailedAsync(
        ProcessingFailedNotification notification,
        CancellationToken cancellationToken)
    {
        using var message = new MailMessage(
            _options.From,
            notification.UserEmail,
            "FIAP X video processing failed",
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
