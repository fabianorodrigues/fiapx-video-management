using FiapX.VideoManagement.Application.Portas;
using System.Security.Claims;

namespace FiapX.VideoManagement.Api.Autenticacao;

public sealed class UsuarioAtualJwt : IUsuarioAtual
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UsuarioAtualJwt(IHttpContextAccessor httpContextAccessor)
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
            throw new InvalidOperationException($"Claim JWT autenticada '{claimType}' é obrigatória.");
        }

        return value;
    }
}
