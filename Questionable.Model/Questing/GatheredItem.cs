namespace Questionable.Model.Questing;

public sealed class GatheredItem
{
    public uint ItemId { get; set; }
    // Preferred 100%-chance leve item; omitted JSON intentionally deserializes to 0.
    public uint AlternativeItemId { get; set; }
    public int ItemCount { get; set; }
    // Omitted JSON intentionally deserializes to 0.
    public ushort Collectability { get; set; }
}

