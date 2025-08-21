using Dalamud.Configuration;

namespace GatheringPathRenderer;

internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string AuthorName { get; set; } = "?";
}
