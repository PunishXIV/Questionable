using System;

namespace LLib.Shop.Model;

public sealed class PurchaseState
{
    public PurchaseState(int desiredItems, int ownedItems)
    {
        DesiredItems = desiredItems;
        OwnedItems = ownedItems;
    }

    public int DesiredItems { get; }
    public int OwnedItems { get; set; }
    public int ItemsLeftToBuy => Math.Max(0, DesiredItems - OwnedItems);
    public bool IsComplete => ItemsLeftToBuy == 0;
    public bool IsAwaitingYesNo { get; set; }
    public DateTime NextStep { get; set; } = DateTime.MinValue;
}
