using FiapX.VideoManagement.Domain.Common;

namespace FiapX.VideoManagement.Domain.Videos;

public enum VideoStatus
{
    Recebido = 1,
    Processando = 2,
    Concluido = 3,
    Erro = 4
}

public static class VideoStatusExtensions
{
    public static string ToContractValue(this VideoStatus status) =>
        status switch
        {
            VideoStatus.Recebido => "RECEBIDO",
            VideoStatus.Processando => "PROCESSANDO",
            VideoStatus.Concluido => "CONCLUIDO",
            VideoStatus.Erro => "ERRO",
            _ => throw new DomainException($"Unsupported video status '{status}'.")
        };

    public static VideoStatus FromContractValue(string value) =>
        value switch
        {
            "RECEBIDO" => VideoStatus.Recebido,
            "PROCESSANDO" => VideoStatus.Processando,
            "CONCLUIDO" => VideoStatus.Concluido,
            "ERRO" => VideoStatus.Erro,
            _ => throw new DomainException($"Unsupported video status '{value}'.")
        };
}
