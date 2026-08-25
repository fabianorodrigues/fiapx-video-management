using FiapX.VideoManagement.Domain.Comum;

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
    public static string ParaValorContrato(this VideoStatus status) =>
        status switch
        {
            VideoStatus.Recebido => "RECEBIDO",
            VideoStatus.Processando => "PROCESSANDO",
            VideoStatus.Concluido => "CONCLUIDO",
            VideoStatus.Erro => "ERRO",
            _ => throw new ExcecaoDominio($"Status de vídeo não suportado: '{status}'.")
        };

    public static VideoStatus DeValorContrato(string value) =>
        value switch
        {
            "RECEBIDO" => VideoStatus.Recebido,
            "PROCESSANDO" => VideoStatus.Processando,
            "CONCLUIDO" => VideoStatus.Concluido,
            "ERRO" => VideoStatus.Erro,
            _ => throw new ExcecaoDominio($"Status de vídeo não suportado: '{value}'.")
        };
}
