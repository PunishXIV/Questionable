using ECommons.ExcelServices;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class InteractJobSelectionTests
{
    [Theory]
    [InlineData(Job.PLD, Job.WAR, Job.DRG)]
    [InlineData(Job.CRP, Job.BSM, Job.ARM)]
    [InlineData(Job.MIN, Job.BTN, Job.FSH)]
    [InlineData(Job.PLD, Job.WAR, Job.ADV)]
    public void IneligibleJob_FallsBackToEligible(Job first, Job second, Job preferred)
    {
        var configuration = ConfigurationWithPreference(preferred);
        Job[] eligibleJobs = [first, second];

        Job selected = SelectJob(eligibleJobs, configuration, EAlliedSociety.None);

        Assert.Equal(first, selected);
        Assert.Contains(selected, eligibleJobs);
    }

    [Theory]
    [InlineData(Job.PLD, Job.WAR)]
    [InlineData(Job.CRP, Job.BSM)]
    [InlineData(Job.MIN, Job.BTN)]
    public void EligiblePreferenceIsHonoured(Job first, Job preferred)
    {
        Assert.Equal(preferred, SelectJob([first, preferred],
            ConfigurationWithPreference(preferred), EAlliedSociety.None));
    }

    [Theory]
    [InlineData(Job.PLD, Job.DRG)]
    [InlineData(Job.CRP, Job.ARM)]
    [InlineData(Job.MIN, Job.FSH)]
    public void OnlyEligibleJobWinsOverPreference(Job eligible, Job preferred)
    {
        Assert.Equal(eligible, SelectJob([eligible],
            ConfigurationWithPreference(preferred), EAlliedSociety.None));
    }

    [Theory]
    [InlineData(true, Job.BSM, Job.BTN, Job.BSM)]
    [InlineData(true, Job.ARM, Job.BTN, Job.MIN)]
    [InlineData(false, Job.BSM, Job.BTN, Job.BTN)]
    [InlineData(false, Job.BSM, Job.FSH, Job.MIN)]
    public void NamazuPreferenceMustStillBeEligible(bool preferCraft, Job crafter, Job gatherer, Job expected)
    {
        var configuration = new Configuration();
        configuration.Advanced.NamazuPreferCraft = preferCraft;
        configuration.General.CraftingJob = crafter;
        configuration.General.GatheringJob = gatherer;

        Assert.Equal(expected, SelectJob([Job.MIN, Job.BTN, Job.BSM], configuration,
            EAlliedSociety.Namazu));
    }

    [Theory]
    [InlineData(false, Job.MIN)]
    [InlineData(true, Job.BSM)]
    public void NamazuAuto_TakesHighestOfKind(bool preferCraft, Job expected)
    {
        var configuration = new Configuration();
        configuration.Advanced.NamazuPreferCraft = preferCraft;

        Assert.Equal(expected, SelectJob([Job.BSM, Job.MIN, Job.BTN, Job.CRP], configuration,
            EAlliedSociety.Namazu));
    }

    [Theory]
    [InlineData(EAlliedSociety.Kobolds, Job.WAR, Job.DRG, false, Job.DRG)]
    [InlineData(EAlliedSociety.Kobolds, Job.DRG, Job.DRG, false, null)]
    [InlineData(EAlliedSociety.Kobolds, Job.WAR, Job.ADV, false, null)]
    [InlineData(EAlliedSociety.Kobolds, Job.WAR, Job.SAM, false, null)]
    [InlineData(EAlliedSociety.Kobolds, Job.CRP, Job.DRG, false, Job.DRG)]
    [InlineData(EAlliedSociety.None, Job.DRG, Job.DRG, false, Job.WAR)]
    [InlineData(EAlliedSociety.None, Job.PLD, Job.DRG, false, null)]
    [InlineData(EAlliedSociety.None, Job.PLD, Job.ADV, true, Job.WAR)]
    [InlineData(EAlliedSociety.None, Job.WAR, Job.ADV, true, null)]
    public void SocietyJob_SwitchesInAndBack(EAlliedSociety society, Job current, Job societyJob, bool lowest,
        Job? expected) =>
        Assert.Equal(expected, Interact.SocietyJobSwitch([Job.WAR, Job.DRG, Job.PLD], current, society, societyJob,
            lowest, () => Job.WAR));

    [Theory]
    [InlineData(Job.FSH, EAlliedSociety.None, true)]
    [InlineData(Job.CRP, EAlliedSociety.None, true)]
    [InlineData(Job.WAR, EAlliedSociety.None, false)]
    [InlineData(Job.FSH, EAlliedSociety.Namazu, false)]
    public void StoryQuest_OnGatherer_NeedsCombat(Job current, EAlliedSociety society, bool expected) =>
        Assert.Equal(expected, Interact.NeedsCombatJob(current, [Job.WAR, Job.CRP, Job.FSH], society));

    [Fact]
    public void GathererOnlyQuest_KeepsGatherer() =>
        Assert.False(Interact.NeedsCombatJob(Job.FSH, [Job.MIN, Job.BTN, Job.FSH], EAlliedSociety.None));

    [Fact]
    public void LowestCombatJob_SkipsCrafters() =>
        Assert.Equal(Job.PLD, Interact.LowestCombatJob([Job.WAR, Job.CRP, Job.PLD, Job.DRG],
            x => x switch { Job.WAR => 100, Job.CRP => 10, Job.PLD => 72, _ => 90 }));

    private static Job SelectJob(Job[] eligibleJobs, Configuration configuration,
        EAlliedSociety alliedSociety) => Interact.SelectQuestJob(eligibleJobs, configuration, alliedSociety,
        eligibleJobs[0] is Job.CRP or Job.BSM or Job.ARM,
        eligibleJobs[0] is Job.MIN or Job.BTN or Job.FSH);

    private static Configuration ConfigurationWithPreference(Job preferred)
    {
        var configuration = new Configuration();
        configuration.General.CombatJob = preferred;
        configuration.General.CraftingJob = preferred;
        configuration.General.GatheringJob = preferred;
        return configuration;
    }
}
