namespace FiapX.VideoManagement.Application.Comum;

public sealed class RequisicaoInvalidaException : Exception
{
    public RequisicaoInvalidaException(string message)
        : base(message)
    {
    }
}

public sealed class RecursoNaoEncontradoException : Exception
{
    public RecursoNaoEncontradoException(string message)
        : base(message)
    {
    }
}

public sealed class ConflitoRecursoException : Exception
{
    public ConflitoRecursoException(string message)
        : base(message)
    {
    }
}

public sealed class ConcorrenciaAtualizacaoVideoException : Exception
{
    public ConcorrenciaAtualizacaoVideoException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ConcorrenciaEventoProcessamentoException : Exception
{
    public ConcorrenciaEventoProcessamentoException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
