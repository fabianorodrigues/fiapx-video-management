using FiapX.VideoManagement.Application.Abstractions;
using System.Security.Claims;

namespace FiapX.VideoManagement.Api.Identity;

public sealed class JwtCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string UserId => GetRequiredClaim("sub");

    public string Email => GetRequiredClaim("email");

    private string GetRequiredClaim(string claimType)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var value = user?.FindFirstValue(claimType);

        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Authenticated JWT claim '{claimType}' is required.");
        }

        return value;
    }
}
