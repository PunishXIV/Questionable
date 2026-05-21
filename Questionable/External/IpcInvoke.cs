using System;
using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging;
namespace Questionable.External;

internal static class IpcInvoke
{
    public static T SafeFunc<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (IpcError)
        {
            return fallback;
        }
    }

    public static T SafeFunc<T>(Func<T> func, T fallback, ILogger logger, string message, params object?[] args)
    {
        try
        {
            return func();
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, message, args);
            return fallback;
        }
    }

    public static void SafeAction(Action action)
    {
        try
        {
            action();
        }
        catch (IpcError)
        {
        }
    }

    public static void SafeAction(Action action, ILogger logger, string message, params object?[] args)
    {
        try
        {
            action();
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, message, args);
        }
    }
}
