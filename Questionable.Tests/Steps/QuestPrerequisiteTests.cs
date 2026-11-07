using Questionable.Data;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestPrerequisiteTests
{
    private readonly FakeState _state = new();

    [Fact]
    public void TableHoldsEveryFormerlyHardCodedQuest()
    {
        ushort[] expected =
        [
            432, 1550, 3200, 4057, 4795, 5469, 576, 4081, 3195, 2387, 1557, 3131, 5459, 3006, 3140, 3178, 3730, 3731,
            3890, 4080, 4524, 4525, 4716, 4816, 5240, 5461, 3153,
        ];

        int[] achievements = [3892, 3893, 3894];

        Assert.Equal(expected.Order(), QuestData.SpecialPrerequisites.Keys.Order());
        Assert.Equal(achievements, Assert.IsType<AchievementPrerequisite>(QuestData.SpecialPrerequisites[5459]).AchievementIds);
        Assert.Equal(40, Assert.IsType<ChocoboRankPrerequisite>(QuestData.SpecialPrerequisites[576]).Rank);
        Assert.Equal(113, Assert.IsType<UnlockLinkPrerequisite>(QuestData.SpecialPrerequisites[3195]).UnlockLinkId);
        Assert.Equal(75, Assert.IsType<DomanEnclavePrerequisite>(QuestData.SpecialPrerequisites[3153]).Milestone);
        Assert.Equal(7, Assert.IsType<MountsPrerequisite>(QuestData.SpecialPrerequisites[5469]).MountIds.Length);
    }

    [Fact]
    public void Achievements_NamedUntilAllDone()
    {
        var prerequisite = new AchievementPrerequisite(3892, 3893);

        Assert.Equal("Needs achievement: A3892, A3893", prerequisite.GetUnmetReason(_state));
        _state.Achievements.UnionWith([3892, 3893]);
        Assert.Null(prerequisite.GetUnmetReason(_state));
    }

    [Fact]
    public void Mounts_ListMissingCountRest()
    {
        var prerequisite = new MountsPrerequisite(1, 2, 3, 4, 5);
        _state.Mounts.Add(2);

        Assert.Equal("Needs mounts: M1, M3 (+2)", prerequisite.GetUnmetReason(_state));
        _state.Mounts.UnionWith([3, 4]);
        Assert.Equal("Needs mounts: M1, M5", prerequisite.GetUnmetReason(_state));
        _state.Mounts.UnionWith([1, 5]);
        Assert.Null(prerequisite.GetUnmetReason(_state));
    }

    [Theory]
    [InlineData(39, false)]
    [InlineData(40, true)]
    [InlineData(41, true)]
    public void ChocoboRacing_NeedsRequiredRank(byte rank, bool met)
    {
        _state.ChocoboRacingRank = rank;

        string? reason = new ChocoboRankPrerequisite(40).GetUnmetReason(_state);

        Assert.Equal(met ? null : "Needs chocobo racing rank 40", reason);
    }

    [Fact]
    public void UnlockLinkMustBeUnlocked()
    {
        var prerequisite = new UnlockLinkPrerequisite(113);

        Assert.Equal("Needs an earlier unlock", prerequisite.GetUnmetReason(_state));
        _state.UnlockLinks.Add(113);
        Assert.Null(prerequisite.GetUnmetReason(_state));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData((byte)74, false)]
    [InlineData((byte)75, true)]
    public void DomanEnclave_NeedsLoadedProgress(byte? milestone, bool met)
    {
        _state.DomanEnclaveMilestone = milestone;

        string? reason = new DomanEnclavePrerequisite(75).GetUnmetReason(_state);

        Assert.Equal(met ? null : "Needs Doman Enclave progress", reason);
    }

    [Fact]
    public void PreviousQuestReasonNamesTheMissingQuests()
    {
        Assert.Equal("Needs quest: Wise Guides", PreviousQuestReason.Format(anyOf: false, ["Wise Guides"]));
        Assert.Equal("Needs quests: A, B", PreviousQuestReason.Format(anyOf: false, ["A", "B"]));
        Assert.Equal("Needs quests: A, B (+2)", PreviousQuestReason.Format(anyOf: false, ["A", "B", "C", "D"]));
        Assert.Equal("Needs one of: A, B", PreviousQuestReason.Format(anyOf: true, ["A", "B"]));
        Assert.Equal("Needs one of: A, B (+1)", PreviousQuestReason.Format(anyOf: true, ["A", "B", "C"]));
    }

    private sealed class FakeState : IPrerequisiteState
    {
        public HashSet<int> Achievements { get; } = [];
        public HashSet<ushort> Mounts { get; } = [];
        public HashSet<ushort> UnlockLinks { get; } = [];
        public byte ChocoboRacingRank { get; set; }
        public byte? DomanEnclaveMilestone { get; set; }

        public bool IsAchievementComplete(int[] achievementIds) => achievementIds.All(Achievements.Contains);
        public bool IsMountUnlocked(ushort mountId) => Mounts.Contains(mountId);
        public bool IsUnlockLinkUnlocked(ushort unlockLinkId) => UnlockLinks.Contains(unlockLinkId);
        public string AchievementName(int achievementId) => $"A{achievementId}";
        public string MountName(ushort mountId) => $"M{mountId}";
    }
}
