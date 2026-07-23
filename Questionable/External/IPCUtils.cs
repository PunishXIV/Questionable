using System;
using ECommons.DalamudServices;
using ECommons.EzIpcManager;
using ECommons.Reflection;
namespace Questionable.External;

internal interface IPCUtils
{
    internal sealed class IPCSubscriber
    {
        internal static bool IsInstalled(string pluginName) => DalamudReflector.TryGetDalamudPlugin(pluginName, out object _, suppressErrors: false, ignoreCache: true);

        internal static Version Version(string pluginName)
        {
            Version _version;
            if (DalamudReflector.TryGetDalamudPlugin(pluginName, out object? dalamudPlugin, suppressErrors: false, ignoreCache: true))
                _version = dalamudPlugin.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            else
                _version = new(0, 0, 0, 0);
            return _version;
        }

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
