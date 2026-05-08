namespace Questionable.Model.Questing;

public sealed class CraftItem
{
    public uint ItemId { get; set; }
    public int ItemCount { get; set; }
    public EItemQuality ItemQuality { get; set; } = EItemQuality.Any;
}
