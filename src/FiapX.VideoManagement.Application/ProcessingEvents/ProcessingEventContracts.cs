namespace FiapX.VideoManagement.Application.ProcessingEvents;

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

public sealed record ProcessingEventHandlingResult(
    ProcessingEventHandlingOutcome Outcome,
    bool StateChanged,
    bool CacheInvalidated,
    bool NotificationAttempted);

public enum ProcessingEventHandlingOutcome
{
    Applied,
    Idempotent,
    Invalid,
    NotFound,
    Conflict
}
