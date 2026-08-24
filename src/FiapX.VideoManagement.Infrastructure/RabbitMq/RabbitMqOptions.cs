using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace FiapX.VideoManagement.Infrastructure.RabbitMq;

public sealed class RabbitMqOptions
{
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "fiapx";
    public string Password { get; init; } = "fiapx_dev_password";
    public string VirtualHost { get; init; } = "/";
    public ushort PrefetchCount { get; init; } = 1;
    public int InitialReconnectDelayMs { get; init; } = 2000;
    public int MaxReconnectDelayMs { get; init; } = 15000;
    public string ClientProvidedName { get; init; } = "fiapx-video-management-service";

    public static RabbitMqOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            HostName = configuration["RABBITMQ_HOST"] ?? "localhost",
            Port = GetInt(configuration, "RABBITMQ_PORT", 5672),
            UserName = configuration["RABBITMQ_USERNAME"]
                ?? configuration["RABBITMQ_DEFAULT_USER"]
                ?? "fiapx",
            Password = configuration["RABBITMQ_PASSWORD"]
                ?? configuration["RABBITMQ_DEFAULT_PASS"]
                ?? "fiapx_dev_password",
            VirtualHost = configuration["RABBITMQ_VHOST"] ?? "/",
            PrefetchCount = (ushort)Math.Clamp(GetInt(configuration, "RABBITMQ_PREFETCH", 1), 1, ushort.MaxValue),
            InitialReconnectDelayMs = GetInt(configuration, "RABBITMQ_INITIAL_RECONNECT_DELAY_MS", 2000),
            MaxReconnectDelayMs = GetInt(configuration, "RABBITMQ_MAX_RECONNECT_DELAY_MS", 15000)
        };

    public ConnectionFactory CreateConnectionFactory() =>
        new()
        {
            HostName = HostName,
            Port = Port,
            UserName = UserName,
            Password = Password,
            VirtualHost = VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            ConsumerDispatchConcurrency = 1,
            ClientProvidedName = ClientProvidedName
        };

    private static int GetInt(IConfiguration configuration, string key, int defaultValue) =>
        int.TryParse(configuration[key], out var value) ? value : defaultValue;
}
