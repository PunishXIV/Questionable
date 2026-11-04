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
    public void IneligiblePreferenceFallsBackToAnEligibleJob(Job first, Job second, Job preferred)
    {
        var configuration = ConfigurationWithPreference(preferred);
        // The preferred job was excluded by the quest's job, gearset or level filter.
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
