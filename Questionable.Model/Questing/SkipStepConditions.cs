using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Questionable.Model.Common;
using Questionable.Model.Questing.Converter;
namespace Questionable.Model.Questing;

public sealed class SkipStepConditions
{
    public bool Never { get; set; }
    public IList<QuestWorkValue?> CompletionQuestVariablesFlags { get; set; } = new List<QuestWorkValue?>();
    public ELockedSkipCondition? Flying { get; set; }
    public ELockedSkipCondition? Chocobo { get; set; }
    public bool? Diving { get; set; }
    public bool NotTargetable { get; set; }
    public List<uint> InTerritory { get; set; } = [];
    public List<uint> NotInTerritory { get; set; } = [];
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
    public List<uint> DutiesUnlocked { get; set; } = [];
    public List<uint> DutiesCompleted { get; set; } = [];

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
               ExtraCondition != null ||
               DutiesUnlocked.Count > 0 ||
               DutiesCompleted.Count > 0;
    }

    public override string ToString()
    {
        var tmp = new string?[] {
            Never ? $"{nameof(Never)}: {Never}" : null,
            CompletionQuestVariablesFlags.Any(x => x != null) ? $"{nameof(CompletionQuestVariablesFlags)}: {string.Join(" ", CompletionQuestVariablesFlags)}" : null,
            Flying != null ? $"{nameof(Flying)}: {Flying}" : null,
            Chocobo != null ? $"{nameof(Chocobo)}: {Chocobo}" : null,
            Diving != null ? $"{nameof(Diving)}: {Diving}" : null,
            NotTargetable ? $"{nameof(NotTargetable)}: {NotTargetable}" : null,
            InTerritory.Count > 0 ? $"{nameof(InTerritory)}: {string.Join(" ", InTerritory)}" : null,
            NotInTerritory.Count > 0 ? $"{nameof(NotInTerritory)}: {string.Join(" ", NotInTerritory)}" : null,
            Item != null ? $"{nameof(Item)}: {Item}" : null,
            QuestsAccepted.Count > 0 ? $"{nameof(QuestsAccepted)}: {string.Join(" ", QuestsAccepted)}" : null,
            QuestsCompleted.Count > 0 ? $"{nameof(QuestsCompleted)}: {string.Join(" ", QuestsCompleted)}" : null,
            NotNamePlateIconId.Count > 0 ? $"{nameof(NotNamePlateIconId)}: {string.Join(" ", NotNamePlateIconId)}" : null,
            AetheryteLocked != null ? $"{nameof(AetheryteLocked)}: {AetheryteLocked}" : null,
            AetheryteUnlocked != null ? $"{nameof(AetheryteUnlocked)}: {AetheryteUnlocked}" : null,
            NearPosition != null ? $"{nameof(NearPosition)}: {NearPosition}" : null,
            NotNearPosition != null ? $"{nameof(NotNearPosition)}: {NotNearPosition}" : null,
            ExtraCondition != null ? $"{nameof(ExtraCondition)}: {ExtraCondition}" : null,
            DutiesUnlocked.Count > 0 ? $"{nameof(DutiesUnlocked)}: {string.Join(" ", DutiesUnlocked)}" : null,
            DutiesCompleted.Count > 0 ? $"{nameof(DutiesCompleted)}: {string.Join(" ", DutiesCompleted)}" : null,
        };
        return string.Join(", ", tmp.Where(x => x != null));
    }
}
