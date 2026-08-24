using Microsoft.Extensions.Configuration;

namespace FiapX.VideoManagement.Infrastructure.RabbitMq;

public sealed class StatusConsumerOptions
{
    public int MaxAttempts { get; init; } = 3;
    public int RetryDelayMs { get; init; } = 5000;

    public static StatusConsumerOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            MaxAttempts = Math.Max(1, GetInt(configuration, "STATUS_MAX_ATTEMPTS", 3)),
            RetryDelayMs = Math.Max(1, GetInt(configuration, "STATUS_RETRY_DELAY_MS", 5000))
        };

    private static int GetInt(IConfiguration configuration, string key, int defaultValue) =>
        int.TryParse(configuration[key], out var value) ? value : defaultValue;
}
