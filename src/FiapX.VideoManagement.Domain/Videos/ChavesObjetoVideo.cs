namespace FiapX.VideoManagement.Domain.Videos;

public static class ChavesObjetoVideo
{
    public static string Original(string userId, Guid videoId) =>
        $"videos/{Required(userId, nameof(userId))}/{videoId}/original.mp4";

    public static string Result(string userId, Guid videoId) =>
        $"results/{Required(userId, nameof(userId))}/{videoId}/resultado.zip";

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} é obrigatório.", name) : value.Trim();
}
