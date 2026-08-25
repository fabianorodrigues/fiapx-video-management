using System.Text.Json;
using FiapX.VideoManagement.Application.Videos.Processamento;
using Microsoft.Extensions.DependencyInjection;

namespace FiapX.VideoManagement.Infrastructure.Mensageria.RabbitMq;

public sealed class DespachanteEventoStatus
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoDespachoEventoStatus> DispatchAsync(
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
                _ => ResultadoDespachoEventoStatus.InvalidContract("Routing key de evento de status desconhecida.")
            };
        }
        catch (JsonException)
        {
            return ResultadoDespachoEventoStatus.InvalidContract("O payload do evento de status não é um JSON válido.");
        }
    }

    private static async Task<ResultadoDespachoEventoStatus> DispatchStartedAsync(
        IServiceProvider serviceProvider,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<VideoProcessingStarted>(body, JsonOptions);
        if (@event is null)
        {
            return ResultadoDespachoEventoStatus.InvalidContract("O payload do evento de início está vazio.");
        }

        var result = await serviceProvider.GetRequiredService<ManipuladorProcessamentoVideoIniciado>()
            .ManipularAsync(@event, cancellationToken);
        return ResultadoDespachoEventoStatus.Handled(result);
    }

    private static async Task<ResultadoDespachoEventoStatus> DispatchCompletedAsync(
        IServiceProvider serviceProvider,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<VideoProcessingCompleted>(body, JsonOptions);
        if (@event is null)
        {
            return ResultadoDespachoEventoStatus.InvalidContract("O payload do evento de conclusão está vazio.");
        }

        var result = await serviceProvider.GetRequiredService<ManipuladorProcessamentoVideoConcluido>()
            .ManipularAsync(@event, cancellationToken);
        return ResultadoDespachoEventoStatus.Handled(result);
    }

    private static async Task<ResultadoDespachoEventoStatus> DispatchFailedAsync(
        IServiceProvider serviceProvider,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<VideoProcessingFailed>(body, JsonOptions);
        if (@event is null)
        {
            return ResultadoDespachoEventoStatus.InvalidContract("O payload do evento de falha está vazio.");
        }

        var result = await serviceProvider.GetRequiredService<ManipuladorProcessamentoVideoFalhou>()
            .ManipularAsync(@event, cancellationToken);
        return ResultadoDespachoEventoStatus.Handled(result);
    }
}

public sealed record ResultadoDespachoEventoStatus(
    bool IsInfrastructureContractValid,
    ResultadoTratamentoEventoProcessamento? HandlingResult,
    string? Error)
{
    public static ResultadoDespachoEventoStatus Handled(ResultadoTratamentoEventoProcessamento handlingResult) =>
        new(true, handlingResult, null);

    public static ResultadoDespachoEventoStatus InvalidContract(string error) =>
        new(false, null, error);
}
