namespace FiapX.VideoManagement.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
