using System.Collections.Generic;
using Questionable.Model.Common.Converter;

namespace Questionable.Model.Questing.Converter;

public sealed class InteractionTypeConverter() : EnumConverter<EInteractionType>(Values)
{
    private static readonly Dictionary<EInteractionType, string> Values = new()
    {
        { EInteractionType.None, "None" },
        { EInteractionType.Interact, "Interact" },
        { EInteractionType.WalkTo, "WalkTo" },
        { EInteractionType.AttuneAethernetShard, "AttuneAethernetShard" },
        { EInteractionType.AttuneAetheryte, "AttuneAetheryte" },
        { EInteractionType.RegisterFreeOrFavoredAetheryte, "RegisterFreeOrFavoredAetheryte" },
        { EInteractionType.AttuneAetherCurrent, "AttuneAetherCurrent" },
        { EInteractionType.Combat, "Combat" },
        { EInteractionType.UseItem, "UseItem" },
        { EInteractionType.EquipItem, "EquipItem" },
        { EInteractionType.PurchaseItem, "PurchaseItem" },
        { EInteractionType.EquipRecommended, "EquipRecommended" },
        { EInteractionType.Say, "Say" },
        { EInteractionType.Emote, "Emote" },
        { EInteractionType.Action, "Action" },
        { EInteractionType.StatusOff, "StatusOff" },
        { EInteractionType.WaitForObjectAtPosition, "WaitForNpcAtPosition" },
        { EInteractionType.WaitForManualProgress, "WaitForManualProgress" },
        { EInteractionType.Duty, "Duty" },
        { EInteractionType.SinglePlayerDuty, "SinglePlayerDuty" },
        { EInteractionType.Jump, "Jump" },
        { EInteractionType.Dive, "Dive" },
        { EInteractionType.Craft, "Craft" },
        { EInteractionType.Gather, "Gather" },
        { EInteractionType.Snipe, "Snipe" },
        { EInteractionType.SwitchClass, "SwitchClass" },
        { EInteractionType.UnlockTaxiStand, "UnlockTaxiStand" },
        { EInteractionType.Instruction, "Instruction" },
        { EInteractionType.AcceptQuest, "AcceptQuest" },
        { EInteractionType.CompleteQuest, "CompleteQuest" },
    };
}
