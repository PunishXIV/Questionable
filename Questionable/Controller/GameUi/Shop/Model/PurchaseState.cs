namespace Questionable.Controller.GameUi.Shop.Model;

public sealed class PurchaseState(int desiredItems, int ownedItems)
{
    public int DesiredItems { get; } = desiredItems;
    public int OwnedItems { get; set; } = ownedItems;
    public int ItemsLeftToBuy => Math.Max(0, DesiredItems - OwnedItems);
    public bool IsComplete => ItemsLeftToBuy == 0;
    public bool IsAwaitingYesNo { get; set; }
    public DateTime NextStep { get; set; } = DateTime.MinValue;
}
