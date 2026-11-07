namespace Questionable.Data;

// Unlock conditions the quest sheets don't describe. GetUnmetReason is null when the condition is met.
internal abstract record QuestPrerequisite
{
    public abstract string? GetUnmetReason(IPrerequisiteState state);
}

internal sealed record AchievementPrerequisite(params int[] AchievementIds) : QuestPrerequisite
{
    public override string? GetUnmetReason(IPrerequisiteState state) =>
        state.IsAchievementComplete(AchievementIds)
            ? null
            : _LF("Needs achievement: {0}", string.Join(", ", AchievementIds.Select(state.AchievementName)));
}

internal sealed record MountsPrerequisite(params ushort[] MountIds) : QuestPrerequisite
{
    private const int ShownNames = 2;

    public override string? GetUnmetReason(IPrerequisiteState state)
    {
        ushort[] missing = MountIds.Where(x => !state.IsMountUnlocked(x)).ToArray();
        if (missing.Length == 0)
            return null;

        string names = string.Join(", ", missing.Take(ShownNames).Select(state.MountName));
        return missing.Length > ShownNames
            ? _LF("Needs mounts: {0} (+{1})", names, missing.Length - ShownNames)
            : _LF("Needs mounts: {0}", names);
    }
}

internal sealed record ChocoboRankPrerequisite(byte Rank) : QuestPrerequisite
{
    public override string? GetUnmetReason(IPrerequisiteState state) =>
        state.ChocoboRacingRank >= Rank ? null : _LF("Needs chocobo racing rank {0}", Rank);
}

internal sealed record UnlockLinkPrerequisite(ushort UnlockLinkId) : QuestPrerequisite
{
    public override string? GetUnmetReason(IPrerequisiteState state) =>
        state.IsUnlockLinkUnlocked(UnlockLinkId) ? null : _L("Needs an earlier unlock");
}

internal sealed record DomanEnclavePrerequisite(byte Milestone) : QuestPrerequisite
{
    public override string? GetUnmetReason(IPrerequisiteState state) =>
        state.DomanEnclaveMilestone >= Milestone ? null : _L("Needs Doman Enclave progress");
}

internal static class PreviousQuestReason
{
    private const int ShownNames = 2;

    public static string Format(bool anyOf, IReadOnlyList<string> missingNames)
    {
        if (missingNames.Count == 0)
            return _L("Prev quest");

        string names = string.Join(", ", missingNames.Take(ShownNames));
        int hidden = missingNames.Count - ShownNames;
        if (anyOf)
            return hidden > 0 ? _LF("Needs one of: {0} (+{1})", names, hidden) : _LF("Needs one of: {0}", names);
        if (hidden > 0)
            return _LF("Needs quests: {0} (+{1})", names, hidden);
        return missingNames.Count == 1 ? _LF("Needs quest: {0}", names) : _LF("Needs quests: {0}", names);
    }
}

internal interface IPrerequisiteState
{
    byte ChocoboRacingRank { get; }

    // Null while the game hasn't loaded the enclave's state.
    byte? DomanEnclaveMilestone { get; }

    bool IsAchievementComplete(int[] achievementIds);
    bool IsMountUnlocked(ushort mountId);
    bool IsUnlockLinkUnlocked(ushort unlockLinkId);
    string AchievementName(int achievementId);
    string MountName(ushort mountId);
}
