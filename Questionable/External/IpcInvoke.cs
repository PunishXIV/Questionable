using System.Reflection;
using Dalamud.Plugin.Ipc.Exceptions;
namespace Questionable.External;

internal static class IpcInvoke
{
    public static T SafeFunc<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception e) when (IsSafeIpcFailure(e))
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
        catch (IpcNotReadyError)
        {
            return fallback;
        }
        catch (Exception e) when (IsSafeIpcFailure(e))
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
        catch (Exception e) when (IsSafeIpcFailure(e))
        {
        }
    }

    public static void SafeAction(Action action, ILogger logger, string message, params object?[] args)
    {
        try
        {
            action();
        }
        catch (IpcNotReadyError)
        {
        }
        catch (Exception e) when (IsSafeIpcFailure(e))
        {
            logger.LogWarning(e, message, args);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the game thread. Used for dispose-time IPC and client-struct
    /// access: <see cref="IAsyncDalamudPlugin.DisposeAsync"/> runs off-thread, and subscriber plugins
    /// often read <c>LocalPlayer</c> (which throws if not on the framework thread).
    /// </summary>
    public static void TryOnFrameworkThread(IFramework framework, Action action, ILogger? logger = null)
    {
        try
        {
            if (framework.IsInFrameworkUpdateThread)
            {
                action();
                return;
            }

            if (framework.IsFrameworkUnloading)
                return;

            framework.RunOnFrameworkThread(action).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            logger?.LogDebug(e, "Game-thread call failed");
        }
    }

    // Dalamud IPC surfaces a subscriber's exception as TargetInvocationException, not IpcError.
    private static bool IsSafeIpcFailure(Exception e) =>
        e is IpcError or TargetInvocationException;
}
