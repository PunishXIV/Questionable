namespace Questionable.External;

internal interface IPCUtils
{
    internal sealed class IPCSubscriber
    {
        internal static bool IsInstalled(string pluginName) => DalamudReflector.TryGetDalamudPlugin(pluginName, out object _, suppressErrors: false, ignoreCache: true);

        internal static Version? Version(string pluginName)
        {
            Version? _version = null;
            if (DalamudReflector.TryGetDalamudPlugin(pluginName, out object? dalamudPlugin, suppressErrors: false, ignoreCache: true))
                _version = dalamudPlugin.GetType().Assembly.GetName().Version;
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
