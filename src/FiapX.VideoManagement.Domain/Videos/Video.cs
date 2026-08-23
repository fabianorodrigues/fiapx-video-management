using FiapX.VideoManagement.Domain.Common;

namespace FiapX.VideoManagement.Domain.Videos;

public sealed class Video
{
    private Video()
    {
        UserId = string.Empty;
        UserEmail = string.Empty;
        OriginalFileName = string.Empty;
        OriginalObjectKey = string.Empty;
    }

    private Video(
        Guid id,
        string userId,
        string userEmail,
        string originalFileName,
        string originalObjectKey,
        string resultObjectKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = Required(userId, nameof(userId), 100);
        UserEmail = Required(userEmail, nameof(userEmail), 255);
        OriginalFileName = Required(originalFileName, nameof(originalFileName), 255);
        OriginalObjectKey = Required(originalObjectKey, nameof(originalObjectKey), 500);
        ResultObjectKey = Required(resultObjectKey, nameof(resultObjectKey), 500);
        CreatedAt = createdAt;
        Status = VideoStatus.Recebido;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public string UserEmail { get; private set; }
    public string OriginalFileName { get; private set; }
    public string OriginalObjectKey { get; private set; }
    public string? ResultObjectKey { get; private set; }
    public VideoStatus Status { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public DateTimeOffset? ProcessingFinishedAt { get; private set; }
    public uint Version { get; private set; }

    public static Video Register(
        Guid id,
        string userId,
        string userEmail,
        string originalFileName,
        string originalObjectKey,
        string resultObjectKey,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("VideoId is required.");
        }

        return new Video(id, userId, userEmail, originalFileName, originalObjectKey, resultObjectKey, createdAt);
    }

    public bool BelongsTo(string userId) =>
        string.Equals(UserId, userId, StringComparison.Ordinal);

    public void MarkProcessing(DateTimeOffset startedAt)
    {
        EnsureStatus(VideoStatus.Recebido, "Only received videos can start processing.");

        Status = VideoStatus.Processando;
        ProcessingStartedAt = startedAt;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarkCompleted(string resultObjectKey, DateTimeOffset finishedAt)
    {
        EnsureStatus(VideoStatus.Processando, "Only processing videos can be completed.");

        Status = VideoStatus.Concluido;
        ResultObjectKey = Required(resultObjectKey, nameof(resultObjectKey), 500);
        ProcessingFinishedAt = finishedAt;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarkFailed(string sanitizedErrorMessage, DateTimeOffset finishedAt)
    {
        EnsureStatus(VideoStatus.Processando, "Only processing videos can fail.");

        Status = VideoStatus.Erro;
        ErrorCode = "PROCESSING_FAILED";
        ErrorMessage = Required(sanitizedErrorMessage, nameof(sanitizedErrorMessage), 1000);
        ProcessingFinishedAt = finishedAt;
    }

    public void MarkCompletedFromProcessingEvent(string resultObjectKey, DateTimeOffset finishedAt)
    {
        EnsureStatusForTerminalTransition();
        EnsureFinishedAtDoesNotPrecedeStartedAt(finishedAt);

        Status = VideoStatus.Concluido;
        ResultObjectKey = Required(resultObjectKey, nameof(resultObjectKey), 500);
        ProcessingFinishedAt = finishedAt;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarkFailedFromProcessingEvent(string errorCode, string sanitizedErrorMessage, DateTimeOffset finishedAt)
    {
        EnsureStatusForTerminalTransition();
        EnsureFinishedAtDoesNotPrecedeStartedAt(finishedAt);

        Status = VideoStatus.Erro;
        ErrorCode = Required(errorCode, nameof(errorCode), 100);
        ErrorMessage = Required(sanitizedErrorMessage, nameof(sanitizedErrorMessage), 1000);
        ProcessingFinishedAt = finishedAt;
    }

    public void EnsureCanDownload()
    {
        if (Status != VideoStatus.Concluido)
        {
            throw new DomainException("Video result is not available for download.");
        }

        if (string.IsNullOrWhiteSpace(ResultObjectKey))
        {
            throw new DomainException("Video result object key is missing.");
        }
    }

    private void EnsureStatus(VideoStatus expectedStatus, string message)
    {
        if (Status != expectedStatus)
        {
            throw new DomainException(message);
        }
    }

    private void EnsureStatusForTerminalTransition()
    {
        if (Status is not VideoStatus.Recebido and not VideoStatus.Processando)
        {
            throw new DomainException("Only received or processing videos can reach a terminal processing status.");
        }
    }

    private void EnsureFinishedAtDoesNotPrecedeStartedAt(DateTimeOffset finishedAt)
    {
        if (ProcessingStartedAt.HasValue && finishedAt < ProcessingStartedAt.Value)
        {
            throw new DomainException("Processing finish time cannot precede processing start time.");
        }
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}
