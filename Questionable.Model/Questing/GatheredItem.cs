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
}
