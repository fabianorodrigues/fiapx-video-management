namespace FiapX.VideoManagement.Application.Abstractions;

public interface INotificationSender
{
    Task SendProcessingFailedAsync(ProcessingFailedNotification notification, CancellationToken cancellationToken);
}

public sealed record ProcessingFailedNotification(
    string UserEmail,
    Guid VideoId,
    string ErrorCode,
    string Message);
