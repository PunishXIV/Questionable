namespace Questionable.Controller.Steps;

// A stop the player can fix. Chat shows the reason as a plain line, not as an error.
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
