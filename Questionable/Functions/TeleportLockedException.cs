namespace Questionable.Functions;

public class TeleportLockedException : Exception
{
    public TeleportLockedException()
    {
    }

    public TeleportLockedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public TeleportLockedException(string message) : base(message)
    {
    }
}