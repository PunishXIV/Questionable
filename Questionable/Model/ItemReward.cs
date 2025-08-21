using System;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;

namespace Questionable.Model;

public enum EItemRewardType
{
    Mount,
    Minion,
    OrchestrionRoll,
    TripleTriadCard,
    FashionAccessory,
}

public sealed class ItemRewardDetails(Item item, ElementId elementId)
{
    public uint ItemId { get; } = item.RowId;
    public string Name { get; } = item.Name.ToDalamudString().ToString();
    public TimeSpan CastTime { get; } = TimeSpan.FromSeconds(item.CastTimeSeconds);
    public ElementId ElementId { get; } = elementId;
}

public abstract record ItemReward(ItemRewardDetails Item)
{
    internal static ItemReward? CreateFromItem(Item item, ElementId elementId)
    {
        if (item.ItemAction.ValueNullable?.Type is 1322)
            return new MountReward(new ItemRewardDetails(item, elementId), item.ItemAction.Value.Data[0]);

        if (item.ItemAction.ValueNullable?.Type is 853)
            return new MinionReward(new ItemRewardDetails(item, elementId), item.ItemAction.Value.Data[0]);

        if (item.AdditionalData.GetValueOrDefault<Orchestrion>() is { } orchestrionRoll)
            return new OrchestrionRollReward(new ItemRewardDetails(item, elementId), orchestrionRoll.RowId);

        if (item.AdditionalData.GetValueOrDefault<TripleTriadCard>() is { } tripleTriadCard)
            return new TripleTriadCardReward(new ItemRewardDetails(item, elementId), (ushort)tripleTriadCard.RowId);

        if (item.ItemAction.ValueNullable?.Type is 20086)
            return new FashionAccessoryReward(new ItemRewardDetails(item, elementId), item.ItemAction.Value.Data[0]);

        return null;
    }

    public uint ItemId => Item.ItemId;
    public string Name => Item.Name;
    public ElementId ElementId => Item.ElementId;
    public TimeSpan CastTime => Item.CastTime;
    public abstract EItemRewardType Type { get; }
    public abstract bool IsUnlocked();
}

public sealed record MountReward(ItemRewardDetails Item, uint MountId)
    : ItemReward(Item)
{
    public override EItemRewardType Type => EItemRewardType.Mount;

    public override unsafe bool IsUnlocked()
        => PlayerState.Instance()->IsMountUnlocked(MountId);
}

public sealed record MinionReward(ItemRewardDetails Item, uint MinionId)
    : ItemReward(Item)
{
    public override EItemRewardType Type => EItemRewardType.Minion;

    public override unsafe bool IsUnlocked()
        => UIState.Instance()->IsCompanionUnlocked(MinionId);
}

public sealed record OrchestrionRollReward(ItemRewardDetails Item, uint OrchestrionRollId)
    : ItemReward(Item)
{
    public override EItemRewardType Type => EItemRewardType.OrchestrionRoll;

    public override unsafe bool IsUnlocked() =>
        PlayerState.Instance()->IsOrchestrionRollUnlocked(OrchestrionRollId);
}

public sealed record TripleTriadCardReward(ItemRewardDetails Item, ushort TripleTriadCardId)
    : ItemReward(Item)
{
    public override EItemRewardType Type => EItemRewardType.TripleTriadCard;

    public override unsafe bool IsUnlocked() =>
        UIState.Instance()->IsTripleTriadCardUnlocked(TripleTriadCardId);
}

public sealed record FashionAccessoryReward(ItemRewardDetails Item, uint AccessoryId)
    : ItemReward(Item)
{
    public override EItemRewardType Type => EItemRewardType.FashionAccessory;

    public override unsafe bool IsUnlocked() =>
        PlayerState.Instance()->IsOrnamentUnlocked(AccessoryId);
}
