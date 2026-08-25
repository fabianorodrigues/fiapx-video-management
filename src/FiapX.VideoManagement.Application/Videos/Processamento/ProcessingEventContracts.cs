namespace FiapX.VideoManagement.Application.Videos.Processamento;

public sealed record VideoProcessingStarted(
    Guid EventId,
    Guid VideoId,
    string UserId,
    DateTimeOffset OccurredAt);

public sealed record VideoProcessingCompleted(
    Guid EventId,
    Guid VideoId,
    string UserId,
    string ResultObjectKey,
    DateTimeOffset OccurredAt);

public sealed record VideoProcessingFailed(
    Guid EventId,
    Guid VideoId,
    string UserId,
    string ErrorCode,
    string? ErrorMessage,
    DateTimeOffset OccurredAt);

public sealed record ResultadoTratamentoEventoProcessamento(
    DesfechoTratamentoEventoProcessamento Outcome,
    bool StateChanged,
    bool CacheInvalidated,
    bool NotificationAttempted);

public enum DesfechoTratamentoEventoProcessamento
{
    Applied,
    Idempotent,
    Invalid,
    NotFound,
    Conflict
}
