using System.Text;
using FiapX.VideoManagement.Infrastructure.RabbitMq;
using RabbitMQ.Client;

namespace FiapX.VideoManagement.Tests;

public sealed class RabbitMqStatusConsumerTests
{
    [Fact]
    public async Task Dispatcher_rejects_unknown_routing_key_as_infrastructure_contract_error()
    {
        var dispatcher = new StatusEventDispatcher();

        var result = await dispatcher.DispatchAsync(
            serviceProvider: new EmptyServiceProvider(),
            routingKey: "video.processing.unknown",
            body: Encoding.UTF8.GetBytes("{}"),
            CancellationToken.None);

        Assert.False(result.IsInfrastructureContractValid);
        Assert.Null(result.HandlingResult);
    }

    [Fact]
    public async Task Dispatcher_rejects_malformed_status_payload_before_handler_resolution()
    {
        var dispatcher = new StatusEventDispatcher();

        var result = await dispatcher.DispatchAsync(
            serviceProvider: new EmptyServiceProvider(),
            routingKey: RabbitMqTopology.CompletedRoutingKey,
            body: Encoding.UTF8.GetBytes("{not-json"),
            CancellationToken.None);

        Assert.False(result.IsInfrastructureContractValid);
        Assert.Null(result.HandlingResult);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(2, 2)]
    [InlineData("3", 3)]
    public void Attempt_header_uses_original_as_first_attempt_and_parses_common_types(object? value, int expected)
    {
        Dictionary<string, object?>? headers = value is null
            ? null
            : new Dictionary<string, object?> { [RabbitMqAttemptHeader.HeaderName] = value };

        Assert.Equal(expected, RabbitMqAttemptHeader.GetCurrentAttempt(headers));
    }

    [Fact]
    public void Retry_properties_preserve_status_metadata_and_replace_attempt_and_expiration()
    {
        var source = new BasicProperties
        {
            ContentType = "application/json",
            MessageId = "event-1",
            CorrelationId = "video-1",
            Type = "VideoProcessingCompleted",
            Expiration = "1000",
            Headers = new Dictionary<string, object?>
            {
                ["x-existing"] = "keep",
                [RabbitMqAttemptHeader.HeaderName] = 1
            }
        };

        var retry = RabbitMqPublishProperties.ForRetry(source, nextAttempt: 2, retryDelayMs: 5000);

        Assert.True(retry.Persistent);
        Assert.Equal("application/json", retry.ContentType);
        Assert.Equal("event-1", retry.MessageId);
        Assert.Equal("video-1", retry.CorrelationId);
        Assert.Equal("VideoProcessingCompleted", retry.Type);
        Assert.Equal("5000", retry.Expiration);
        Assert.Equal("keep", retry.Headers!["x-existing"]);
        Assert.Equal(2, retry.Headers[RabbitMqAttemptHeader.HeaderName]);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
