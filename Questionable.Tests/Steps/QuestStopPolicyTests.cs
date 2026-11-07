using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestStopPolicyTests
{
    [Theory]
    [InlineData("UI stop", true, false)]
    [InlineData("ui STOP", true, false)]
    [InlineData("ESC pressed", true, false)]
    [InlineData("esc PRESSED", true, false)]
    [InlineData("Skip", true, true)]
    [InlineData("SkipNx", true, true)]
    [InlineData("Task failed to update", true, true)]
    [InlineData("Logged out", true, true)]
    [InlineData("Skip", false, false)]
    [InlineData("Task failed to update", false, false)]
    public void OnlyEligibleAutomaticStopsRunEffects(string label, bool automatic, bool expected)
    {
        var configuration = Config();
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), effects.Add,
            NullLogger<QuestStopPolicy>.Instance);

        policy.Apply(label, automatic);

        Assert.Equal(expected ? ["notify", "/after-stop"] : Array.Empty<string>(), effects);
    }

    [Theory]
    [InlineData(false, "/after-stop")]
    [InlineData(true, "after-stop")]
    [InlineData(true, "")]
    public void DisabledCommands_StillNotify(bool enabled, string command)
    {
        var configuration = Config();
        configuration.Stop.RunCommandAfterStop = enabled;
        configuration.Stop.CommandAfterStop = command;
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), effects.Add,
            NullLogger<QuestStopPolicy>.Instance);

        policy.Apply("Skip", true);

        Assert.Equal(["notify"], effects);
    }

    [Fact]
    public void ReadsLiveCommandSettingsForEachStop()
    {
        var configuration = Config();
        List<string> commands = [];
        var policy = new QuestStopPolicy(configuration, _ => { }, commands.Add, NullLogger<QuestStopPolicy>.Instance);
        policy.Apply("Skip", true);
        configuration.Stop.CommandAfterStop = "/changed";
        policy.Apply("Skip", true);
        configuration.Stop.RunCommandAfterStop = false;
        policy.Apply("Skip", true);

        Assert.Equal(["/after-stop", "/changed"], commands);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EffectFailure_LoggedOthersStillRun(bool failNotification, bool failCommand)
    {
        List<string> effects = [];
        var logger = Substitute.For<ILogger<QuestStopPolicy>>();
        var policy = new QuestStopPolicy(Config(), _ =>
        {
            effects.Add("notify");
            if (failNotification)
                throw new InvalidOperationException("notification failed");
        }, command =>
        {
            effects.Add(command);
            if (failCommand)
                throw new InvalidOperationException("command failed");
        }, logger);

        policy.Apply("Skip", true);

        Assert.Equal(["notify", "/after-stop"], effects);
        Assert.Equal((failNotification ? 1 : 0) + (failCommand ? 1 : 0),
            logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "Log"));
    }

    private static Configuration Config()
    {
        var configuration = new Configuration();
        configuration.Stop.RunCommandAfterStop = true;
        configuration.Stop.CommandAfterStop = "/after-stop";
        return configuration;
    }
}
