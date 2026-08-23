using Microsoft.Extensions.Configuration;

namespace FiapX.VideoManagement.Infrastructure.Mail;

public sealed class SmtpNotificationOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string From { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public static SmtpNotificationOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            Host = configuration["SMTP_HOST"] ?? "localhost",
            Port = configuration.GetValue("SMTP_PORT", 1025),
            From = configuration["SMTP_FROM"] ?? "no-reply@fiapx.local",
            Timeout = TimeSpan.FromSeconds(configuration.GetValue("SMTP_TIMEOUT_SECONDS", 5))
        };
}
