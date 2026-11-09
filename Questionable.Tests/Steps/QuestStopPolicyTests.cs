using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
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
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance);

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
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance);

        policy.Apply("Skip", true);

        Assert.Equal(["notify"], effects);
    }

    [Fact]
    public void ReadsLiveCommandSettingsForEachStop()
    {
        var configuration = Config();
        List<string> commands = [];
        var policy = new QuestStopPolicy(configuration, _ => { }, _ => { }, commands.Add, Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance);
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
        }, _ => { }, command =>
        {
            effects.Add(command);
            if (failCommand)
                throw new InvalidOperationException("command failed");
        }, Substitute.For<IChatGui>(), logger);

        policy.Apply("Skip", true);

        Assert.Equal(["notify", "/after-stop"], effects);
        Assert.Equal((failNotification ? 1 : 0) + (failCommand ? 1 : 0),
            logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "Log"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopPoint_AlwaysNotifies(bool setting)
    {
        Reports reports = new(setting);

        reports.Policy.ReportStopPoint(TestQuest());

        Assert.Equal(["Completed quest 'Test', which is configured as a stopping point."], reports.Printed);
        Assert.Equal(reports.Printed, reports.Notified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OtherStops_NotifyFollowsSetting(bool setting)
    {
        Reports reports = new(setting);

        reports.Policy.ReportLevelReached(90);
        reports.Policy.ReportStopAfterQuest(TestQuest());
        reports.Policy.ReportStopBeforeTeleport(Substitute.For<ITask>());

        Assert.Equal(["Reached or exceeded target level 50.", "Completed quest 'Test', stopping as requested.",
            "Stopping before teleport as requested."], reports.Printed);
        Assert.Equal(setting ? reports.Printed : [], reports.Notified);
    }

    [Theory]
    [InlineData(true, "Accepted quest 'Test', which is configured as a stopping point.")]
    [InlineData(false, "Accepted quest 'Test', stopping as requested.")]
    public void AcceptStop_NeverNotifies(bool configured, string expected)
    {
        Reports reports = new(notifySetting: true);

        reports.Policy.ReportAcceptStop(TestQuest(), configured);

        Assert.Equal([expected], reports.Printed);
        Assert.Empty(reports.Notified);
    }

    [Fact]
    public void LevelStops_PrintOnly()
    {
        Reports reports = new(notifySetting: true);

        reports.Policy.ReportLevelTooLow(TestQuest().Info, 10);
        reports.Policy.ReportMainScenarioLocked(TestQuest().Info, 11);

        Assert.Equal(["Stopped: 'Test' needs level 20 (you're level 10).",
            "Stopped: 'Test' needs level 20 (you're level 11)."], reports.Printed);
        Assert.Empty(reports.Notified);
    }

    [Fact]
    public void Errors_PrintOnly()
    {
        Reports reports = new(notifySetting: true);
        QuestController.QuestProgress progress = new(TestQuest(), 2, 3);

        reports.Policy.ReportNoSequence(progress);
        reports.Policy.ReportNoStep(progress);
        reports.Policy.ReportDeathLimit(5);

        Assert.Equal(["Stopped: no path for 'Test' sequence 2. Try Reload Data or report it with Stuck?",
            "Stopped: no path for 'Test' sequence 2 step 3. Try Reload Data or report it with Stuck?",
            "You died 5 times, manual intervention needed."], reports.Errors);
        Assert.Empty(reports.Printed);
        Assert.Empty(reports.Notified);
    }

    [Fact]
    public void TasksFailed_UsesFailureNotify()
    {
        Reports reports = new(notifySetting: true);

        reports.Policy.ReportTasksFailed(new InvalidOperationException(), null);

        Assert.Equal(["Failed to start next task sequence, please check /xllog for details."], reports.Errors);
        Assert.Empty(reports.Notified);
        Assert.Equal(["Failed to start next task sequence, stopping. Please try Reload Data, fixing manually, or reporting an issue with the 'Stuck?' button."],
            reports.FailureNotified);
    }

    private static Quest TestQuest()
    {
        Quest quest = QuestTestData.CreateQuest(new QuestId(500), QuestTestData.Seq(0, new QuestStep()));
        quest.Info.Name.Returns("Test");
        quest.Info.Level.Returns((ushort)20);
        return quest;
    }

    private sealed class Reports
    {
        private readonly IChatGui _chat = Substitute.For<IChatGui>();

        public Reports(bool notifySetting)
        {
            var configuration = new Configuration();
            configuration.Stop.TargetLevel = 50;
            configuration.Notifications.NotifyOnStopCondition = notifySetting;
            Policy = new QuestStopPolicy(configuration, FailureNotified.Add, Notified.Add, _ => { }, _chat,
                NullLogger<QuestStopPolicy>.Instance);
        }

        public QuestStopPolicy Policy { get; }
        public List<string> FailureNotified { get; } = [];
        public List<string> Notified { get; } = [];
        public List<string> Printed => Calls(nameof(IChatGui.Print));
        public List<string> Errors => Calls(nameof(IChatGui.PrintError));

        private List<string> Calls(string method) => _chat.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == method)
            .Select(call => (string)call.GetArguments()[0]!)
            .ToList();
    }

    private static Configuration Config()
    {
        var configuration = new Configuration();
        configuration.Stop.RunCommandAfterStop = true;
        configuration.Stop.CommandAfterStop = "/after-stop";
        return configuration;
    }
}
