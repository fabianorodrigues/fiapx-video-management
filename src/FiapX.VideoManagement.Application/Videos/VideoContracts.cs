namespace FiapX.VideoManagement.Application.Videos;

public sealed record CreateVideoRequest(string FileName, string ContentType);

public sealed record CreateVideoResponse(
    Guid VideoId,
    string Status,
    string UploadUrl,
    int ExpiresInSeconds);

public sealed record VideoResponse(
    Guid VideoId,
    string UserId,
    string OriginalFileName,
    string OriginalObjectKey,
    string? ResultObjectKey,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessingStartedAt,
    DateTimeOffset? ProcessingFinishedAt);

public sealed record DownloadVideoResponse(string DownloadUrl, int ExpiresInSeconds);
