using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FiapX.VideoManagement.Infrastructure.RabbitMq;

public sealed class RabbitMqStatusConsumer(
    IServiceScopeFactory scopeFactory,
    RabbitMqOptions rabbitMqOptions,
    StatusConsumerOptions consumerOptions,
    StatusEventDispatcher dispatcher,
    ILogger<RabbitMqStatusConsumer> logger,
    ILogger<ConfirmedRabbitMqPublisher> publisherLogger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RabbitMQ status consumer stopped unexpectedly and will reconnect.");
                await Task.Delay(TimeSpan.FromMilliseconds(rabbitMqOptions.InitialReconnectDelayMs), stoppingToken);
            }
        }
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        await using var connection = await RabbitMqConnection.ConnectWithRetryAsync(rabbitMqOptions, logger, stoppingToken);
        await using var consumerChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: 1),
            stoppingToken);
        await using var publisherChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true,
                consumerDispatchConcurrency: 1),
            stoppingToken);

        await ValidateTopologyAsync(consumerChannel, stoppingToken);
        await consumerChannel.BasicQosAsync(0, rabbitMqOptions.PrefetchCount, global: false, stoppingToken);

        var publisher = new ConfirmedRabbitMqPublisher(publisherChannel, publisherLogger);
        var consumer = new AsyncEventingBasicConsumer(consumerChannel);
        consumer.ReceivedAsync += async (_, args) =>
            await HandleDeliveryAsync(consumerChannel, publisher, args, stoppingToken);

        var consumerTag = await consumerChannel.BasicConsumeAsync(
            RabbitMqTopology.StatusQueue,
            autoAck: false,
            consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "VideoManagementService consuming RabbitMQ status queue. Queue={Queue} ConsumerTag={ConsumerTag} Prefetch={Prefetch}",
            RabbitMqTopology.StatusQueue,
            consumerTag,
            rabbitMqOptions.PrefetchCount);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("VideoManagementService stopping RabbitMQ status consumer. ConsumerTag={ConsumerTag}", consumerTag);
            throw;
        }
    }

    private static async Task ValidateTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclarePassiveAsync(RabbitMqTopology.EventsExchange, cancellationToken);
        await channel.ExchangeDeclarePassiveAsync(RabbitMqTopology.StatusRetryExchange, cancellationToken);
        await channel.ExchangeDeclarePassiveAsync(RabbitMqTopology.StatusDlx, cancellationToken);
        await channel.QueueDeclarePassiveAsync(RabbitMqTopology.StatusQueue, cancellationToken);
        await channel.QueueDeclarePassiveAsync(RabbitMqTopology.StatusDlq, cancellationToken);
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        ConfirmedRabbitMqPublisher publisher,
        BasicDeliverEventArgs args,
        CancellationToken stoppingToken)
    {
        var body = args.Body.ToArray();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await dispatcher.DispatchAsync(
                scope.ServiceProvider,
                args.RoutingKey,
                body,
                stoppingToken);

            if (!result.IsInfrastructureContractValid)
            {
                logger.LogWarning(
                    "Status event has invalid infrastructure contract and will be sent to status DLQ. RoutingKey={RoutingKey} Reason={Reason}",
                    args.RoutingKey,
                    result.Error);

                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, stoppingToken);
                return;
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken);

            logger.LogInformation(
                "Status event acknowledged. RoutingKey={RoutingKey} Outcome={Outcome}",
                args.RoutingKey,
                result.HandlingResult?.Outcome);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Status event handling was cancelled by shutdown. Message will be redelivered. RoutingKey={RoutingKey}",
                args.RoutingKey);
        }
        catch (Exception ex)
        {
            await HandleTransientFailureAsync(channel, publisher, args, body, ex, stoppingToken);
        }
    }

    private async Task HandleTransientFailureAsync(
        IChannel channel,
        ConfirmedRabbitMqPublisher publisher,
        BasicDeliverEventArgs args,
        byte[] body,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var currentAttempt = RabbitMqAttemptHeader.GetCurrentAttempt(args.BasicProperties.Headers);
        if (currentAttempt < consumerOptions.MaxAttempts)
        {
            var nextAttempt = currentAttempt + 1;
            await publisher.PublishRetryAsync(
                args.RoutingKey,
                args.BasicProperties,
                body,
                nextAttempt,
                consumerOptions.RetryDelayMs,
                cancellationToken);

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);

            logger.LogWarning(
                exception,
                "Transient status event failure was sent to retry. RoutingKey={RoutingKey} Attempt={Attempt} NextAttempt={NextAttempt}",
                args.RoutingKey,
                currentAttempt,
                nextAttempt);
            return;
        }

        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);

        logger.LogWarning(
            exception,
            "Status event retries were exhausted and message was sent to status DLQ. RoutingKey={RoutingKey} Attempt={Attempt}",
            args.RoutingKey,
            currentAttempt);
    }
}
