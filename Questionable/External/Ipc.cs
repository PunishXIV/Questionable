using System.Xml.Linq;

namespace Questionable.External;

internal abstract class Ipc
{
    public abstract string InternalName { get; }
    public abstract bool IsReady();
    public abstract Version? GetVersion();
    public bool IsLoaded => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded);
}
