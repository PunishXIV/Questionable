namespace Questionable.Model.Questing;

public sealed class GatheredItem
{
    public uint ItemId { get; set; }
    /// <summary>
    /// For leves that allow you to gather two items with different chance percentage, this is the preferred item if the gathering chance is 100% (after buffs). May be omitted from quest path JSON and will intentionally deserialize to 0 instead of null.
    /// </summary>
    public uint AlternativeItemId { get; set; }
    public int ItemCount { get; set; }
    /// <summary>
    /// May be omitted from quest path JSON and will intentionally deserialize to 0 instead of null.
    /// </summary>
    public ushort Collectability { get; set; }
    public FishingOptions? FishingOptions { get; set; }
}

public class FishingOptions
{
    public uint BaitId { get; set; }
    /// <summary>
    /// Omit to enable all hook types and hooksets.
    /// </summary>
    public HookType? HookType { get; set; }
    /// <summary>
    /// exported Autohook preset (alternative to FishingData.cs)
    /// </summary>
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
