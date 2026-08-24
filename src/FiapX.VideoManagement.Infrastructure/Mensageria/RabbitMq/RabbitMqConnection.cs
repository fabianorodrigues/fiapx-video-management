using RabbitMQ.Client;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Infrastructure.Mensageria.RabbitMq;

public static class RabbitMqConnection
{
    public static async Task<IConnection> ConnectWithRetryAsync(
        RabbitMqOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var factory = options.CreateConnectionFactory();
        var delay = TimeSpan.FromMilliseconds(options.InitialReconnectDelayMs);
        var maxDelay = TimeSpan.FromMilliseconds(options.MaxReconnectDelayMs);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await factory.CreateConnectionAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Conexão RabbitMQ do consumidor de status indisponível. Host={RabbitMqHost} Port={RabbitMqPort} VHost={RabbitMqVHost} RetryDelayMs={RetryDelayMs}",
                    options.HostName,
                    options.Port,
                    options.VirtualHost,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }
    }
}
