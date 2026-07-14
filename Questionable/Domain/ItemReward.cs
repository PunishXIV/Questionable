using System;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Domain;

public abstract record ItemReward(ItemRewardDetails Item)
{
    public uint ItemId => Item.ItemId;
    public string Name => Item.Name;
    public ElementId ElementId => Item.ElementId;
    public TimeSpan CastTime => Item.CastTime;
    public abstract EItemRewardType Type { get; }
    internal static bool IsValidCoffer(Item item) =>
        item.ItemAction.RowId is 1085 or 388 or 367 && item.ItemUICategory.RowId is 61;

    internal static ItemReward? CreateFromItem(Item item, ElementId elementId)
    {
        if (IsValidCoffer(item))
            return new CofferReward(new(item, elementId));

        if (item.ItemAction.Value is var itemAction &&
            itemAction.Action.Value is var action)
        {
            if (action.RowId is 1322)
                return new MountReward(new(item, elementId), item.ItemAction.Value.Data[0]);

            if (action.RowId is 853)
                return new MinionReward(new(item, elementId), item.ItemAction.Value.Data[0]);

            if (action.RowId is 20086)
                return new FashionAccessoryReward(new(item, elementId), item.ItemAction.Value.Data[0]);

            if (action.RowId is 25183)
                return new OrchestrionRollReward(new(item, elementId), item.AdditionalData.RowId);

            if (action.RowId is 3357)
                return new TripleTriadCardReward(new(item, elementId), (ushort)item.AdditionalData.RowId);

            if (action.RowId is 2633)
                return new UnlockLinkReward(new(item, elementId), (ushort)item.ItemAction.Value.Data[0]);
        }

        return null;
    }
    public abstract bool IsUnlocked();
    public override string ToString() => $"{nameof(Type)}: {Name}";

    internal sealed record MountReward(ItemRewardDetails Item, uint MountId)
        : ItemReward(Item)
    {
        public override EItemRewardType Type => EItemRewardType.Mount;

        public override unsafe bool IsUnlocked() => PlayerState.Instance()->IsMountUnlocked(MountId);
    }

    internal sealed record MinionReward(ItemRewardDetails Item, uint MinionId)
        : ItemReward(Item)
    {
        public override EItemRewardType Type => EItemRewardType.Minion;

        public override unsafe bool IsUnlocked() => UIState.Instance()->IsCompanionUnlocked(MinionId);
    }

    internal sealed record OrchestrionRollReward(ItemRewardDetails Item, uint OrchestrionRollId)
        : ItemReward(Item)
    {
        public override EItemRewardType Type => EItemRewardType.OrchestrionRoll;

        public override unsafe bool IsUnlocked() => PlayerState.Instance()->IsOrchestrionRollUnlocked(OrchestrionRollId);
    }

    internal sealed record TripleTriadCardReward(ItemRewardDetails Item, ushort TripleTriadCardId)
        : ItemReward(Item)
    {
        public override EItemRewardType Type => EItemRewardType.TripleTriadCard;

        public override unsafe bool IsUnlocked() => UIState.Instance()->IsTripleTriadCardUnlocked(TripleTriadCardId);
    }

    internal sealed record FashionAccessoryReward(ItemRewardDetails Item, uint AccessoryId)
        : ItemReward(Item)
    {
        public override EItemRewardType Type => EItemRewardType.FashionAccessory;

        public override unsafe bool IsUnlocked() => PlayerState.Instance()->IsOrnamentUnlocked(AccessoryId);
    }

    internal sealed record CofferReward(ItemRewardDetails Item) : ItemReward(Item)
    {
        public override EItemRewardType Type => EItemRewardType.Coffer;

        public override bool IsUnlocked() => false;
    }

    internal sealed record UnlockLinkReward(ItemRewardDetails Item, ushort UnlockLinkId) : ItemReward(Item)
    {
        public override EItemRewardType Type => EItemRewardType.UnlockLink;

        public override unsafe bool IsUnlocked() => UIState.Instance()->IsUnlockLinkUnlocked(UnlockLinkId);
    }
}
