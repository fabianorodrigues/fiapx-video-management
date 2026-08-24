using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.Videos.Processamento;

public sealed class ManipuladorProcessamentoVideoIniciado : ManipuladorEventoProcessamentoBase
{
    public ManipuladorProcessamentoVideoIniciado(
        IRepositorioVideo videos,
        ICacheVideo cache,
        ILogger<ManipuladorProcessamentoVideoIniciado> logger)
        : base(videos, cache, logger)
    {
    }

    public Task<ResultadoTratamentoEventoProcessamento> ManipularAsync(
        VideoProcessingStarted @event,
        CancellationToken cancellationToken)
    {
        var envelope = new EnvelopeEventoProcessamento(
            @event.EventId,
            @event.VideoId,
            @event.UserId,
            @event.OccurredAt,
            nameof(VideoProcessingStarted));

        return HandleAsync(envelope, video => Evaluate(video, @event), afterStateChangeAsync: null, cancellationToken);
    }

    private static AvaliacaoEventoProcessamento Evaluate(Video video, VideoProcessingStarted @event)
    {
        var occurredAt = NormalizeOccurredAt(@event.OccurredAt);

        return video.Status switch
        {
            VideoStatus.Recebido => ApplyStarted(video, occurredAt),
            VideoStatus.Processando => Idempotent(shouldInvalidateCache: true),
            VideoStatus.Concluido or VideoStatus.Erro => Idempotent(
                shouldInvalidateCache: false,
                warningMessage: "Evento de início não pode regredir um status terminal do vídeo."),
            _ => Conflict("Evento de início encontrou um status de vídeo não suportado.")
        };
    }

    private static AvaliacaoEventoProcessamento ApplyStarted(Video video, DateTimeOffset occurredAt)
    {
        video.MarcarProcessando(occurredAt);
        return Applied();
    }
}
