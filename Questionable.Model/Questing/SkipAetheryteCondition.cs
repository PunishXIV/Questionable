using System.Collections.Generic;
using System.Text.Json.Serialization;
using Questionable.Model.Common;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

public sealed class SkipAetheryteCondition
{
    public bool Never { get; set; }
    public bool InSameTerritory { get; set; }
    public List<ushort> InTerritory { get; set; } = new();

    [JsonConverter(typeof(ElementIdListConverter))]
    public List<ElementId> QuestsAccepted { get; set; } = new();

    [JsonConverter(typeof(ElementIdListConverter))]
    public List<ElementId> QuestsCompleted { get; set; } = new();

    public EAetheryteLocation? AetheryteLocked { get; set; }
    public EAetheryteLocation? AetheryteUnlocked { get; set; }
    public bool RequiredQuestVariablesNotMet { get; set; }
    public NearPositionCondition? NearPosition { get; set; }
    public NearPositionCondition? NotNearPosition { get; set; }
    public EExtraSkipCondition? ExtraCondition { get; set; }
}
