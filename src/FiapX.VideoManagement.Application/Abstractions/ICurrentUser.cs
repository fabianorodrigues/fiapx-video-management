namespace FiapX.VideoManagement.Application.Abstractions;

public interface ICurrentUser
{
    string UserId { get; }
    string Email { get; }
}
