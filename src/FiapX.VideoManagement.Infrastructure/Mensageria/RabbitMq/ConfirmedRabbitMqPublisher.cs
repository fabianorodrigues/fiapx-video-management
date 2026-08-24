using RabbitMQ.Client;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Infrastructure.Mensageria.RabbitMq;

public sealed class ConfirmedRabbitMqPublisher(
    IChannel channel,
    ILogger<ConfirmedRabbitMqPublisher> logger)
{
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    public async Task PublishRetryAsync(
        string routingKey,
        IReadOnlyBasicProperties sourceProperties,
        ReadOnlyMemory<byte> body,
        int nextAttempt,
        int retryDelayMs,
        CancellationToken cancellationToken)
    {
        var properties = RabbitMqPublishProperties.ForRetry(sourceProperties, nextAttempt, retryDelayMs);

        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            await channel.BasicPublishAsync(
                RabbitMqTopology.StatusRetryExchange,
                routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Falha ao publicar retry de status no RabbitMQ. RoutingKey={RoutingKey} MessageId={MessageId} CorrelationId={CorrelationId}",
                routingKey,
                properties.MessageId,
                properties.CorrelationId);
            throw;
        }
        finally
        {
            _publishLock.Release();
        }
    }
}
