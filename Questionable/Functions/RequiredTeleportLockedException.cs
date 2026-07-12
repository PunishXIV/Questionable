using System;

namespace Questionable.Functions;

public class RequiredTeleportLockedException : Exception
{
    public RequiredTeleportLockedException()
    {
    }

    public RequiredTeleportLockedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public RequiredTeleportLockedException(string message) : base(message)
    {
    }
}