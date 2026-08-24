using FiapX.VideoManagement.Application.Portas;
using Microsoft.Extensions.Configuration;

namespace FiapX.VideoManagement.Infrastructure.Autenticacao;

public sealed class UsuarioAtualDesenvolvimento : IUsuarioAtual
{
    public UsuarioAtualDesenvolvimento(IConfiguration configuration)
    {
        UserId = configuration["DEV_USER_ID"] ?? "dev-user-1";
        Email = configuration["DEV_USER_EMAIL"] ?? "dev.user@fiapx.local";
    }

    public string UserId { get; }
    public string Email { get; }
}
