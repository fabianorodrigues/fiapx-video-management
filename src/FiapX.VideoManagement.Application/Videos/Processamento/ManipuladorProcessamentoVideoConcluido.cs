using FiapX.VideoManagement.Application.Portas;
using FiapX.VideoManagement.Domain.Videos;
using Microsoft.Extensions.Logging;

namespace FiapX.VideoManagement.Application.Videos.Processamento;

public sealed class ManipuladorProcessamentoVideoConcluido : ManipuladorEventoProcessamentoBase
{
    public ManipuladorProcessamentoVideoConcluido(
        IRepositorioVideo videos,
        ICacheVideo cache,
        ILogger<ManipuladorProcessamentoVideoConcluido> logger)
        : base(videos, cache, logger)
    {
    }

    public Task<ResultadoTratamentoEventoProcessamento> ManipularAsync(
        VideoProcessingCompleted @event,
        CancellationToken cancellationToken)
    {
        var envelope = new EnvelopeEventoProcessamento(
            @event.EventId,
            @event.VideoId,
            @event.UserId,
            @event.OccurredAt,
            nameof(VideoProcessingCompleted));

        return HandleAsync(envelope, video => Evaluate(video, @event), afterStateChangeAsync: null, cancellationToken);
    }

    private static AvaliacaoEventoProcessamento Evaluate(Video video, VideoProcessingCompleted @event)
    {
        if (string.IsNullOrWhiteSpace(@event.ResultObjectKey))
        {
            return Invalid("Evento de conclusão está sem ResultObjectKey.");
        }

        var expectedResultObjectKey = ChavesObjetoVideo.Result(@event.UserId, @event.VideoId);
        if (!string.Equals(@event.ResultObjectKey.Trim(), expectedResultObjectKey, StringComparison.Ordinal))
        {
            return Invalid("ResultObjectKey do evento de conclusão não corresponde à chave esperada do resultado.");
        }

        var occurredAt = NormalizeOccurredAt(@event.OccurredAt);
        if (video.ProcessingStartedAt.HasValue && occurredAt < video.ProcessingStartedAt.Value)
        {
            return Invalid("Timestamp do evento de conclusão é anterior ao início do processamento.");
        }

        return video.Status switch
        {
            VideoStatus.Recebido or VideoStatus.Processando => ApplyCompleted(video, expectedResultObjectKey, occurredAt),
            VideoStatus.Concluido when string.Equals(video.ResultObjectKey, expectedResultObjectKey, StringComparison.Ordinal) =>
                Idempotent(shouldInvalidateCache: true),
            VideoStatus.Concluido => Conflict("Evento de conclusão conflita com a chave de resultado já persistida."),
            VideoStatus.Erro => Conflict("Evento de conclusão não pode sobrescrever status terminal de erro."),
            _ => Conflict("Evento de conclusão encontrou um status de vídeo não suportado.")
        };
    }

    private static AvaliacaoEventoProcessamento ApplyCompleted(
        Video video,
        string expectedResultObjectKey,
        DateTimeOffset occurredAt)
    {
        video.MarcarConcluidoPorEventoProcessamento(expectedResultObjectKey, occurredAt);
        return Applied();
    }
}
