using System.Globalization;
using RabbitMQ.Client;

namespace FiapX.VideoManagement.Infrastructure.RabbitMq;

public static class RabbitMqPublishProperties
{
    public static BasicProperties ForRetry(
        IReadOnlyBasicProperties source,
        int nextAttempt,
        int retryDelayMs)
    {
        var headers = CopyHeaders(source.Headers);
        headers[RabbitMqAttemptHeader.HeaderName] = nextAttempt;

        return new BasicProperties
        {
            ContentType = source.ContentType,
            ContentEncoding = source.ContentEncoding,
            Persistent = true,
            MessageId = source.MessageId,
            CorrelationId = source.CorrelationId,
            Type = source.Type,
            Headers = headers,
            Expiration = retryDelayMs.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static Dictionary<string, object?> CopyHeaders(IDictionary<string, object?>? source)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (source is null)
        {
            return headers;
        }

        foreach (var item in source)
        {
            if (!string.Equals(item.Key, "expiration", StringComparison.OrdinalIgnoreCase))
            {
                headers[item.Key] = item.Value;
            }
        }

        return headers;
    }
}
