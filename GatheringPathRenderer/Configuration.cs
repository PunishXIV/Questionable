using Dalamud.Configuration;
namespace GatheringPathRenderer;

internal sealed class Configuration : IPluginConfiguration
{
    public string AuthorName { get; set; } = "?";
    public int Version { get; set; } = 1;
    public bool ShowOverlay { get; set; } = true;
}
