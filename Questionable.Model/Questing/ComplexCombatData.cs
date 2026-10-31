using System.Collections.Generic;
namespace Questionable.Model.Questing;

public sealed class ComplexCombatData
{
    public uint DataId { get; set; }
    public uint? NameId { get; set; }

    /// <summary>
    ///     Only used together with <see cref="IgnoreQuestMarker" />: after killing this many, the entry counts as done.
    ///     Otherwise the quest marker on the enemy decides when enough have been killed.
    /// </summary>
    public uint? MinimumKillCount { get; set; }

    /// <summary>
    ///     If a reward item has been set, this is (ping allowing) given to the player before the dead enemy despawns.
    /// </summary>
    public uint? RewardItemId { get; set; }
    public int? RewardItemCount { get; set; }
    public IList<QuestWorkValue?> CompletionQuestVariablesFlags { get; set; } = [];
    public bool IgnoreQuestMarker { get; set; }
}
