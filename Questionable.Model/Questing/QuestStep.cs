using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using Questionable.Model.Common;
using Questionable.Model.Common.Converter;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
public sealed class QuestStep
{
    public const float DefaultStopDistance = 3f;
    public const int VesperBayAetheryteTicket = 30362;

    public uint? DataId { get; set; }

    [JsonConverter(typeof(VectorConverter))]
    public Vector3? Position { get; set; }

    public float? StopDistance { get; set; }
    public ushort TerritoryId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public EInteractionType InteractionType { get; set; }

    public float? NpcWaitDistance { get; set; }
    public ushort? TargetTerritoryId { get; set; }
    public float? DelaySecondsAtStart { get; set; }
    public uint? PickUpItemId { get; set; }

    public bool Disabled { get; set; }
    public bool DisableNavmesh { get; set; }
    public bool? Mount { get; set; }
    public bool? Fly { get; set; }
    public bool? Land { get; set; }
    public bool? Sprint { get; set; }
    public bool? IgnoreDistanceToObject { get; set; }
    public bool? RestartNavigationIfCancelled { get; set; }
    public string? Comment { get; set; }

    /// <summary>
    /// Only used when attuning to an aetheryte.
    /// </summary>
    public EAetheryteLocation? Aetheryte { get; set; }

    /// <summary>
    /// Only used when attuning to an aethernet shard.
    /// </summary>
    [JsonConverter(typeof(AethernetShardConverter))]
    public EAetheryteLocation? AethernetShard { get; set; }

    public EAetheryteLocation? AetheryteShortcut { get; set; }

    public AethernetShortcut? AethernetShortcut { get; set; }
    public uint? AetherCurrentId { get; set; }

    public uint? ItemId { get; set; }
    public bool? GroundTarget { get; set; }
    public int? ItemCount { get; set; }

    public EEmote? Emote { get; set; }
    public ChatMessage? ChatMessage { get; set; }
    public EAction? Action { get; set; }
    public EStatus? Status { get; set; }
    public EExtendedClassJob TargetClass { get; set; } = EExtendedClassJob.None;
    public byte? TaxiStandId { get; set; }

    public EEnemySpawnType? EnemySpawnType { get; set; }
    public List<uint> KillEnemyDataIds { get; set; } = [];
    public List<ComplexCombatData> ComplexCombatData { get; set; } = [];
    public CombatItemUse? CombatItemUse { get; set; }
    public float? CombatDelaySecondsAtStart { get; set; }

    public JumpDestination? JumpDestination { get; set; }
    public DutyOptions? DutyOptions { get; set; }
    public SinglePlayerDutyOptions? SinglePlayerDutyOptions { get; set; }
    public byte SinglePlayerDutyIndex => SinglePlayerDutyOptions?.Index ?? 0;
    public SkipConditions? SkipConditions { get; set; }

    public List<List<QuestWorkValue>?> RequiredQuestVariables { get; set; } = new();
    public List<EExtendedClassJob> RequiredCurrentJob { get; set; } = [];
    public List<EExtendedClassJob> RequiredQuestAcceptedJob { get; set; } = [];
    public List<GatheredItem> ItemsToGather { get; set; } = [];
    public List<QuestWorkValue?> CompletionQuestVariablesFlags { get; set; } = [];
    public List<DialogueChoice> DialogueChoices { get; set; } = [];
    public List<uint> PointMenuChoices { get; set; } = [];
    public PurchaseMenu? PurchaseMenu { get; set; }

    // TODO: Not implemented
    [JsonConverter(typeof(ElementIdConverter))]
    public ElementId? PickUpQuestId { get; set; }

    [JsonConverter(typeof(ElementIdConverter))]
    public ElementId? TurnInQuestId { get; set; }

    [JsonConverter(typeof(ElementIdConverter))]
    public ElementId? NextQuestId { get; set; }

    [JsonConstructor]
    public QuestStep()
    {
    }

    public QuestStep(EInteractionType interactionType, uint? dataId, Vector3? position, ushort territoryId)
    {
        InteractionType = interactionType;
        DataId = dataId;
        Position = position;
        TerritoryId = territoryId;
    }

    public float CalculateActualStopDistance()
    {
        if (StopDistance is { } stopDistance)
            return stopDistance;

        return InteractionType switch
        {
            EInteractionType.WalkTo => 0.25f,
            EInteractionType.AttuneAetheryte or EInteractionType.RegisterFreeOrFavoredAetheryte => 10f,
            _ => DefaultStopDistance
        };
    }

    /// <summary>
    /// Only relevant for the step 0 in sequence 0: Whether this step is valid for teleporting to it.
    /// </summary>
    /// <returns></returns>
    public bool IsTeleportableForPriorityQuests()
    {
        if (AetheryteShortcut != null)
            return true;

        if (InteractionType == EInteractionType.UseItem && ItemId == VesperBayAetheryteTicket)
            return true;

        return false;
    }
}
