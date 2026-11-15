using System.Collections.Generic;
namespace Questionable.Model.Questing;

public sealed class ComplexCombatData
{
    public uint DataId { get; set; }
    public uint? NameId { get; set; }

    // Only used with IgnoreQuestMarker; otherwise the enemy's quest marker decides completion.
    public uint? MinimumKillCount { get; set; }

    // Given before the dead enemy despawns.
    public uint? RewardItemId { get; set; }
    public int? RewardItemCount { get; set; }
    public IList<QuestWorkValue?> CompletionQuestVariablesFlags { get; set; } = [];
    public bool IgnoreQuestMarker { get; set; }
}
