using Dalamud.Plugin;
using NSubstitute;
using Questionable.Windows.ConfigComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class PluginSelectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LoadedDevelopmentCopy_IsPreferredOverDisabledCopy(bool developmentCopyFirst, bool requireLoaded)
    {
        var disabledPlugin = CreatePlugin("BossMod", isLoaded: false);
        var developmentPlugin = CreatePlugin("BossMod", isLoaded: true);
        IExposedPlugin[] plugins = developmentCopyFirst
            ? [developmentPlugin, disabledPlugin]
            : [disabledPlugin, developmentPlugin];

        Assert.Same(developmentPlugin, PluginConfigComponent.FindInstalledPlugin(plugins, "BossMod", requireLoaded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedDevelopmentCopyOnly_IsSelected(bool requireLoaded)
    {
        var developmentPlugin = CreatePlugin("BossMod", isLoaded: true);

        Assert.Same(developmentPlugin,
            PluginConfigComponent.FindInstalledPlugin([developmentPlugin], "BossMod", requireLoaded));
    }

    [Fact]
    public void DisabledCopyOnly_IsSelectedForStatus()
    {
        var disabledPlugin = CreatePlugin("BossMod", isLoaded: false);

        Assert.Same(disabledPlugin,
            PluginConfigComponent.FindInstalledPlugin([disabledPlugin], "BossMod", requireLoaded: false));
    }

    [Fact]
    public void DisabledCopyOnly_IsNotSelectedWhenLoadedIsRequired()
    {
        var disabledPlugin = CreatePlugin("BossMod", isLoaded: false);

        Assert.Null(PluginConfigComponent.FindInstalledPlugin([disabledPlugin], "BossMod", requireLoaded: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingPlugin_ReturnsNull(bool requireLoaded)
    {
        Assert.Null(PluginConfigComponent.FindInstalledPlugin([], "BossMod", requireLoaded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedLoadedPlugin_IsNotSelected(bool requireLoaded)
    {
        var unrelatedPlugin = CreatePlugin("vnavmesh", isLoaded: true);

        Assert.Null(PluginConfigComponent.FindInstalledPlugin([unrelatedPlugin], "BossMod", requireLoaded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedLoadedPlugin_DoesNotHideDisabledMatch(bool requireLoaded)
    {
        var unrelatedPlugin = CreatePlugin("vnavmesh", isLoaded: true);
        var disabledPlugin = CreatePlugin("BossMod", isLoaded: false);

        Assert.Same(requireLoaded ? null : disabledPlugin,
            PluginConfigComponent.FindInstalledPlugin([unrelatedPlugin, disabledPlugin], "BossMod", requireLoaded));
    }

    private static IExposedPlugin CreatePlugin(string internalName, bool isLoaded)
    {
        var plugin = Substitute.For<IExposedPlugin>();
        plugin.InternalName.Returns(internalName);
        plugin.IsLoaded.Returns(isLoaded);
        return plugin;
    }
}
