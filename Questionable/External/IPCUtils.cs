namespace Questionable.External;

internal interface IPCUtils
{
    internal sealed class IPCSubscriber
    {
        internal static bool IsInstalled(string pluginName) =>
            Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == pluginName && p.IsLoaded);

        // Internal names, like IsInstalled: display names differ for some plugins ("Pandora's Box", "Gather & Chill").
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
