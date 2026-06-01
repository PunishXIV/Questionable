using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
namespace Questionable.Windows.ConfigComponents;

internal sealed class ProfilesConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration) : ConfigComponent(pluginInterface, configuration)
{

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem("Profiles###Profiles");
        if (!tab)
            return;
    }
}
