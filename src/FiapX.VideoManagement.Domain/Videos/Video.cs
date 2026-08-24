using FiapX.VideoManagement.Domain.Comum;

namespace FiapX.VideoManagement.Domain.Videos;

public sealed class Video
{
    private Video()
    {
        UserId = string.Empty;
        UserEmail = string.Empty;
        OriginalFileName = string.Empty;
        OriginalObjectKey = string.Empty;
    }

    private Video(
        Guid id,
        string userId,
        string userEmail,
        string originalFileName,
        string originalObjectKey,
        string resultObjectKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = Required(userId, nameof(userId), 100);
        UserEmail = Required(userEmail, nameof(userEmail), 255);
        OriginalFileName = Required(originalFileName, nameof(originalFileName), 255);
        OriginalObjectKey = Required(originalObjectKey, nameof(originalObjectKey), 500);
        ResultObjectKey = Required(resultObjectKey, nameof(resultObjectKey), 500);
        CreatedAt = createdAt;
        Status = VideoStatus.Recebido;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public string UserEmail { get; private set; }
    public string OriginalFileName { get; private set; }
    public string OriginalObjectKey { get; private set; }
    public string? ResultObjectKey { get; private set; }
    public VideoStatus Status { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public DateTimeOffset? ProcessingFinishedAt { get; private set; }
    public uint Version { get; private set; }

    public static Video Registrar(
        Guid id,
        string userId,
        string userEmail,
        string originalFileName,
        string originalObjectKey,
        string resultObjectKey,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ExcecaoDominio("VideoId é obrigatório.");
        }

        return new Video(id, userId, userEmail, originalFileName, originalObjectKey, resultObjectKey, createdAt);
    }

    public bool PertenceAoUsuario(string userId) =>
        string.Equals(UserId, userId, StringComparison.Ordinal);

    public void MarcarProcessando(DateTimeOffset startedAt)
    {
        GarantirStatus(VideoStatus.Recebido, "Somente vídeos recebidos podem iniciar processamento.");

        Status = VideoStatus.Processando;
        ProcessingStartedAt = startedAt;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarcarConcluido(string resultObjectKey, DateTimeOffset finishedAt)
    {
        GarantirStatus(VideoStatus.Processando, "Somente vídeos em processamento podem ser concluídos.");

        Status = VideoStatus.Concluido;
        ResultObjectKey = Required(resultObjectKey, nameof(resultObjectKey), 500);
        ProcessingFinishedAt = finishedAt;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarcarErro(string sanitizedErrorMessage, DateTimeOffset finishedAt)
    {
        GarantirStatus(VideoStatus.Processando, "Somente vídeos em processamento podem falhar.");

        Status = VideoStatus.Erro;
        ErrorCode = "PROCESSING_FAILED";
        ErrorMessage = Required(sanitizedErrorMessage, nameof(sanitizedErrorMessage), 1000);
        ProcessingFinishedAt = finishedAt;
    }

    public void MarcarConcluidoPorEventoProcessamento(string resultObjectKey, DateTimeOffset finishedAt)
    {
        GarantirStatusParaTransicaoTerminal();
        GarantirFimNaoAnteriorAoInicio(finishedAt);

        Status = VideoStatus.Concluido;
        ResultObjectKey = Required(resultObjectKey, nameof(resultObjectKey), 500);
        ProcessingFinishedAt = finishedAt;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarcarErroPorEventoProcessamento(string errorCode, string sanitizedErrorMessage, DateTimeOffset finishedAt)
    {
        GarantirStatusParaTransicaoTerminal();
        GarantirFimNaoAnteriorAoInicio(finishedAt);

        Status = VideoStatus.Erro;
        ErrorCode = Required(errorCode, nameof(errorCode), 100);
        ErrorMessage = Required(sanitizedErrorMessage, nameof(sanitizedErrorMessage), 1000);
        ProcessingFinishedAt = finishedAt;
    }

    public void GarantirDownloadDisponivel()
    {
        if (Status != VideoStatus.Concluido)
        {
            throw new ExcecaoDominio("O vídeo ainda não está disponível para download.");
        }

        if (string.IsNullOrWhiteSpace(ResultObjectKey))
        {
            throw new ExcecaoDominio("A chave do resultado do vídeo está ausente.");
        }
    }

    private void GarantirStatus(VideoStatus expectedStatus, string message)
    {
        if (Status != expectedStatus)
        {
            throw new ExcecaoDominio(message);
        }
    }

    private void GarantirStatusParaTransicaoTerminal()
    {
        if (Status is not VideoStatus.Recebido and not VideoStatus.Processando)
        {
            throw new ExcecaoDominio("Somente vídeos recebidos ou em processamento podem chegar a um status terminal.");
        }
    }

    private void GarantirFimNaoAnteriorAoInicio(DateTimeOffset finishedAt)
    {
        if (ProcessingStartedAt.HasValue && finishedAt < ProcessingStartedAt.Value)
        {
            throw new ExcecaoDominio("A data de fim do processamento não pode ser anterior à data de início.");
        }
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ExcecaoDominio($"{name} é obrigatório.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ExcecaoDominio($"{name} deve ter no máximo {maxLength} caracteres.");
        }

        return trimmed;
    }
}
