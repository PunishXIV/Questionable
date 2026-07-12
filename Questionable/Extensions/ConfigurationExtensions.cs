using ECommons.DalamudServices;

namespace Questionable.Extensions;

internal static class ConfigurationExtensions
{
    internal static void Save(this Configuration configuration)
    {
        Svc.PluginInterface.SavePluginConfig(configuration);
    }
}
