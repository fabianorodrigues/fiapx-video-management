using System.Text.Json;
using FiapX.VideoManagement.Application.ProcessingEvents;
using Microsoft.Extensions.DependencyInjection;

namespace FiapX.VideoManagement.Infrastructure.RabbitMq;

public sealed class StatusEventDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StatusEventDispatchResult> DispatchAsync(
        IServiceProvider serviceProvider,
        string routingKey,
        byte[] body,
        CancellationToken cancellationToken)
    {
        try
        {
            return routingKey switch
            {
                RabbitMqTopology.StartedRoutingKey => await DispatchStartedAsync(serviceProvider, body, cancellationToken),
                RabbitMqTopology.CompletedRoutingKey => await DispatchCompletedAsync(serviceProvider, body, cancellationToken),
                RabbitMqTopology.FailedRoutingKey => await DispatchFailedAsync(serviceProvider, body, cancellationToken),
                _ => StatusEventDispatchResult.InvalidContract("Unknown status event routing key.")
            };
        }
        catch (JsonException)
        {
            return StatusEventDispatchResult.InvalidContract("Status event payload is not valid JSON.");
        }
    }

    private static async Task<StatusEventDispatchResult> DispatchStartedAsync(
        IServiceProvider serviceProvider,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<VideoProcessingStarted>(body, JsonOptions);
        if (@event is null)
        {
            return StatusEventDispatchResult.InvalidContract("Started event payload is empty.");
        }

        var result = await serviceProvider.GetRequiredService<VideoProcessingStartedHandler>()
            .HandleAsync(@event, cancellationToken);
        return StatusEventDispatchResult.Handled(result);
    }

    private static async Task<StatusEventDispatchResult> DispatchCompletedAsync(
        IServiceProvider serviceProvider,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<VideoProcessingCompleted>(body, JsonOptions);
        if (@event is null)
        {
            return StatusEventDispatchResult.InvalidContract("Completed event payload is empty.");
        }

        var result = await serviceProvider.GetRequiredService<VideoProcessingCompletedHandler>()
            .HandleAsync(@event, cancellationToken);
        return StatusEventDispatchResult.Handled(result);
    }

    private static async Task<StatusEventDispatchResult> DispatchFailedAsync(
        IServiceProvider serviceProvider,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<VideoProcessingFailed>(body, JsonOptions);
        if (@event is null)
        {
            return StatusEventDispatchResult.InvalidContract("Failed event payload is empty.");
        }

        var result = await serviceProvider.GetRequiredService<VideoProcessingFailedHandler>()
            .HandleAsync(@event, cancellationToken);
        return StatusEventDispatchResult.Handled(result);
    }
}

public sealed record StatusEventDispatchResult(
    bool IsInfrastructureContractValid,
    ProcessingEventHandlingResult? HandlingResult,
    string? Error)
{
    public static StatusEventDispatchResult Handled(ProcessingEventHandlingResult handlingResult) =>
        new(true, handlingResult, null);

    public static StatusEventDispatchResult InvalidContract(string error) =>
        new(false, null, error);
}
