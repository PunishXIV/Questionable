namespace Questionable.Model.Questing;

public sealed class GatheredItem
{
    public uint ItemId { get; set; }
    public uint AlternativeItemId { get; set; }
    public int ItemCount { get; set; }
    public ushort Collectability { get; set; }
}
