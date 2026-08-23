namespace FiapX.VideoManagement.Application.Common;

public sealed class RequestValidationException : Exception
{
    public RequestValidationException(string message)
        : base(message)
    {
    }
}

public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class ResourceConflictException : Exception
{
    public ResourceConflictException(string message)
        : base(message)
    {
    }
}

public sealed class VideoUpdateConcurrencyException : Exception
{
    public VideoUpdateConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ProcessingEventConcurrencyException : Exception
{
    public ProcessingEventConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
