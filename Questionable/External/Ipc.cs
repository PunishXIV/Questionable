using System.Reflection;
using Dalamud.Plugin.Ipc.Exceptions;

namespace Questionable.External;

internal abstract class Ipc
{
    public abstract string InternalName { get; }
    public abstract bool IsReady();
    public abstract Version? GetVersion();
}

internal interface IPCUtils
{
    internal sealed class IPCSubscriber
    {
        internal static bool IsInstalled(string pluginName) =>
            Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == pluginName && p.IsLoaded);

        internal static Version? Version(string pluginName) =>
            Svc.PluginInterface.InstalledPlugins.FirstOrDefault(p => p.InternalName == pluginName && p.IsLoaded)?.Version;

        internal static void DisposeAll(EzIPCDisposalToken[] _disposalTokens)
        {
            foreach (EzIPCDisposalToken token in _disposalTokens)
            {
                try
                {
                    token.Dispose();
                }
                catch (Exception ex)
                {
                    Svc.Log.Error($"Error while unregistering IPC: {ex}");
                }
            }
        }
    }
}

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

    /// <summary>Runs <paramref name="action"/> on the framework thread, or inline if already on it.</summary>
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

    // Subscriber exceptions come back as TargetInvocationException, not IpcError.
    internal static bool IsSafeIpcFailure(Exception e) =>
        e is IpcError or TargetInvocationException;
}

internal enum ExternalOperationStatus
{
    Unknown = 0,
    Running = 1,
    CancellationRequested = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
