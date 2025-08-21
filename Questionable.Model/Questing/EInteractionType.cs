using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(InteractionTypeConverter))]
public enum EInteractionType
{
    None,
    Interact,
    WalkTo,
    AttuneAethernetShard,
    AttuneAetheryte,
    RegisterFreeOrFavoredAetheryte,
    AttuneAetherCurrent,
    Combat,
    UseItem,
    EquipItem,
    PurchaseItem,
    EquipRecommended,
    Say,
    Emote,
    Action,
    StatusOff,
    WaitForObjectAtPosition,
    WaitForManualProgress,
    Duty,
    SinglePlayerDuty,
    Jump,
    Dive,
    Craft,
    Gather,
    Snipe,
    SwitchClass,
    UnlockTaxiStand,

    /// <summary>
    /// Needs to be manually continued.
    /// </summary>
    Instruction,

    AcceptQuest,
    CompleteQuest,
}
