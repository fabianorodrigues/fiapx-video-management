namespace FiapX.VideoManagement.Application.Videos;

public static class VideoCacheKeys
{
    public static string Videos(string userId) => $"videos:{userId}";

    public static string Video(string userId, Guid videoId) => $"video:{userId}:{videoId}";
}
