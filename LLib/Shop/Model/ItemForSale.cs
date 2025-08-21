namespace LLib.Shop.Model;

public sealed class ItemForSale
{
    public required int Position { get; init; }
    public required uint ItemId { get; init; }
    public required string? ItemName { get; init; }
    public required uint Price { get; init; }
    public required uint OwnedItems { get; init; }
}
