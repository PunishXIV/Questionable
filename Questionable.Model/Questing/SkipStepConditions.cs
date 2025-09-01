using Questionable.Model.Common;
using Questionable.Model.Questing.Converter;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

public sealed class SkipStepConditions
{
    public bool Never { get; set; }
    public IList<QuestWorkValue?> CompletionQuestVariablesFlags { get; set; } = new List<QuestWorkValue?>();
    public ELockedSkipCondition? Flying { get; set; }
    public ELockedSkipCondition? Chocobo { get; set; }
    public bool? Diving { get; set; }
    public bool NotTargetable { get; set; }
    public List<ushort> InTerritory { get; set; } = [];
    public List<ushort> NotInTerritory { get; set; } = [];
    public SkipItemConditions? Item { get; set; }

    [JsonConverter(typeof(ElementIdListConverter))]
    public List<ElementId> QuestsAccepted { get; set; } = [];

    [JsonConverter(typeof(ElementIdListConverter))]
    public List<ElementId> QuestsCompleted { get; set; } = [];

    public List<uint> NotNamePlateIconId { get; set; } = [];

    public EAetheryteLocation? AetheryteLocked { get; set; }
    public EAetheryteLocation? AetheryteUnlocked { get; set; }
    public NearPositionCondition? NearPosition { get; set; }
    public NearPositionCondition? NotNearPosition { get; set; }
    public EExtraSkipCondition? ExtraCondition { get; set; }

    public bool HasSkipConditions()
    {
        if (Never)
            return false;
        return (CompletionQuestVariablesFlags.Count > 0 && CompletionQuestVariablesFlags.Any(x => x != null)) ||
               Flying != null ||
               Chocobo != null ||
               Diving != null ||
               NotTargetable ||
               InTerritory.Count > 0 ||
               NotInTerritory.Count > 0 ||
               Item != null ||
               QuestsAccepted.Count > 0 ||
               QuestsCompleted.Count > 0 ||
               NotNamePlateIconId.Count > 0 ||
               AetheryteLocked != null ||
               AetheryteUnlocked != null ||
               NearPosition != null ||
               NotNearPosition != null ||
               ExtraCondition != null;
    }

    public override string ToString()
    {
        return
            $"{nameof(Never)}: {Never}, {nameof(CompletionQuestVariablesFlags)}: {CompletionQuestVariablesFlags}, {nameof(Flying)}: {Flying}, {nameof(Chocobo)}: {Chocobo}, {nameof(Diving)}: {Diving}, {nameof(NotTargetable)}: {NotTargetable}, {nameof(InTerritory)}: {string.Join(" ", InTerritory)}, {nameof(NotInTerritory)}: {string.Join(" ", NotInTerritory)}, {nameof(Item)}: {Item}, {nameof(QuestsAccepted)}: {string.Join(" ", QuestsAccepted)}, {nameof(QuestsCompleted)}: {string.Join(" ", QuestsCompleted)}, {nameof(NotNamePlateIconId)}: {string.Join(" ", NotNamePlateIconId)}, {nameof(NearPosition)}: {NearPosition}, {nameof(ExtraCondition)}: {ExtraCondition}";
    }
}
