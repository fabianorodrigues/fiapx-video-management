namespace FiapX.VideoManagement.Infrastructure.RabbitMq;

public static class RabbitMqTopology
{
    public const string EventsExchange = "video.events";
    public const string StatusQueue = "video.status-updates";
    public const string StatusRetryExchange = "video.status.retry.exchange";
    public const string StatusDlx = "video.status.dlx";
    public const string StatusDlq = "video.status-updates.dlq";

    public const string StartedRoutingKey = "video.processing.started";
    public const string CompletedRoutingKey = "video.processing.completed";
    public const string FailedRoutingKey = "video.processing.failed";
}
