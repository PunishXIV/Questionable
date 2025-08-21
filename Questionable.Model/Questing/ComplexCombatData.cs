using System.Collections.Generic;

namespace Questionable.Model.Questing;

public sealed class ComplexCombatData
{
    public uint DataId { get; set; }
    public uint? NameId { get; set; }

    // TODO Use this
    public uint? MinimumKillCount { get; set; }

    /// <summary>
    /// If a reward item has been set, this is (ping allowing) given to the player before the dead enemy despawns.
    /// </summary>
    public uint? RewardItemId { get; set; }
    public int? RewardItemCount { get; set; }
    public IList<QuestWorkValue?> CompletionQuestVariablesFlags { get; set; } = new List<QuestWorkValue?>();
    public bool IgnoreQuestMarker { get; set; }
}
