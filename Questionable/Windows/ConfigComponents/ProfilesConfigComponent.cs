using Dalamud.Interface.Utility.Raii;
namespace Questionable.Windows.ConfigComponents;

internal sealed class ProfilesConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration) : ConfigComponent(pluginInterface, configuration)
{

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Profiles") + "###Profiles");
        if (!tab)
            return;
    }
}
