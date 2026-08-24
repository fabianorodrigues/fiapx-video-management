using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace FiapX.VideoManagement.Infrastructure.Mensageria.RabbitMq;

public static class RabbitMqAttemptHeader
{
    public const string HeaderName = "x-attempt";

    public static int GetCurrentAttempt(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(HeaderName, out var value))
        {
            return 1;
        }

        return TryConvert(value, out var attempt) && attempt > 0 ? attempt : 1;
    }

    private static bool TryConvert(object? value, out int attempt)
    {
        switch (value)
        {
            case null:
                attempt = 0;
                return false;
            case int intValue:
                attempt = intValue;
                return true;
            case long longValue when longValue <= int.MaxValue && longValue >= int.MinValue:
                attempt = (int)longValue;
                return true;
            case short shortValue:
                attempt = shortValue;
                return true;
            case byte byteValue:
                attempt = byteValue;
                return true;
            case sbyte sbyteValue:
                attempt = sbyteValue;
                return true;
            case uint uintValue when uintValue <= int.MaxValue:
                attempt = (int)uintValue;
                return true;
            case ulong ulongValue when ulongValue <= int.MaxValue:
                attempt = (int)ulongValue;
                return true;
            case byte[] bytes:
                return TryConvertBytes(bytes, out attempt);
            case ReadOnlyMemory<byte> memory:
                return TryConvertBytes(memory.ToArray(), out attempt);
            case string text:
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out attempt);
            default:
                attempt = 0;
                return false;
        }
    }

    private static bool TryConvertBytes(byte[] bytes, out int attempt)
    {
        var text = Encoding.UTF8.GetString(bytes);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out attempt))
        {
            return true;
        }

        if (bytes.Length == sizeof(int))
        {
            attempt = BinaryPrimitives.ReadInt32BigEndian(bytes);
            return true;
        }

        if (bytes.Length == sizeof(long))
        {
            var value = BinaryPrimitives.ReadInt64BigEndian(bytes);
            if (value <= int.MaxValue && value >= int.MinValue)
            {
                attempt = (int)value;
                return true;
            }
        }

        attempt = 0;
        return false;
    }
}
