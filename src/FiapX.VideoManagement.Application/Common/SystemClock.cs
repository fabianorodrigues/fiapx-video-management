using FiapX.VideoManagement.Application.Abstractions;

namespace FiapX.VideoManagement.Application.Common;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
