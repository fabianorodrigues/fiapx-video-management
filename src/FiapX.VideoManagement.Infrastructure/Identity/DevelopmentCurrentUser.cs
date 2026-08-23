using FiapX.VideoManagement.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace FiapX.VideoManagement.Infrastructure.Identity;

public sealed class DevelopmentCurrentUser : ICurrentUser
{
    public DevelopmentCurrentUser(IConfiguration configuration)
    {
        UserId = configuration["DEV_USER_ID"] ?? "dev-user-1";
        Email = configuration["DEV_USER_EMAIL"] ?? "dev.user@fiapx.local";
    }

    public string UserId { get; }
    public string Email { get; }
}
