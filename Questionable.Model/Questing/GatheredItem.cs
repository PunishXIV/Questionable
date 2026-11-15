namespace Questionable.Model.Questing;

public sealed class GatheredItem
{
    public uint ItemId { get; set; }
    // Preferred 100%-chance leve item; omitted JSON intentionally deserializes to 0.
    public uint AlternativeItemId { get; set; }
    public int ItemCount { get; set; }
    // Omitted JSON intentionally deserializes to 0.
    public ushort Collectability { get; set; }
    public FishingOptions? FishingOptions { get; set; }
}

public class FishingOptions
{
    public uint BaitId { get; set; }
    // Omit to enable all hook types and hooksets.
    public HookType? HookType { get; set; }
    // Exported Autohook preset, instead of FishingData.cs.
    public string? Preset { get; set; }
}

public class HookType
{
    public HookTypeFilter? Normal { get; set; }
    public HookTypeFilter? Double { get; set; }
    public HookTypeFilter? Triple { get; set; }
}

public class Hookset
{
    public bool? Weak { get; set; }
    public bool? Strong { get; set; }
    public bool? Legendary { get; set; }
}
