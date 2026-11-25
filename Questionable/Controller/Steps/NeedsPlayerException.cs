namespace Questionable.Controller.Steps;

public sealed class NeedsPlayerException : TaskException
{
    public NeedsPlayerException()
    {
    }

    public NeedsPlayerException(string message)
        : base(message)
    {
    }

    public NeedsPlayerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
