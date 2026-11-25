using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Domain;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestStopPolicyTests
{
    [Theory]
    [InlineData("UI stop", true, false)]
    [InlineData("ESC pressed", true, false)]
    [InlineData("Skip", true, true)]
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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void EndActions_NeedFinishedOrSetting(bool finished, bool afterProblems, bool expected)
    {
        var configuration = Config();
        configuration.Stop.RunAfterProblemStop = afterProblems;
        configuration.Stop.EndAction = Configuration.EEndAction.LogOut;

        Assert.Equal(expected ? ["notify", "/after-stop", "/logout"] : ["notify"],
            Apply(configuration, finished));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void RequestedStop_AlertsOnlyIfAsked(bool requested, bool alertSetting, bool expectedNotify)
    {
        var configuration = Config();
        configuration.Notifications.NotifyOnRequestedStop = alertSetting;
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance);

        policy.Apply("Stop command", true, requested: requested);

        Assert.Equal(expectedNotify, effects.Contains("notify"));
    }

    [Theory]
    [InlineData("Stop command", true)]
    [InlineData("Player moved", false)]
    public void RequestedStop_SkipsEndActions(string label, bool requested)
    {
        var configuration = Config();
        configuration.Stop.RunAfterProblemStop = true;
        configuration.Stop.EndAction = Configuration.EEndAction.LogOut;
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance);

        policy.Apply(label, true, requested: requested);

        Assert.DoesNotContain("/after-stop", effects);
        Assert.DoesNotContain("/logout", effects);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "notify,/after-stop")]
    public void IdleRun_SkipsEndActions(bool listRun, string expected)
    {
        var configuration = Config();
        configuration.Stop.EndAction = Configuration.EEndAction.LogOut;
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance)
        {
            IsListRun = listRun,
            StopHandler = canContinue => canContinue,
        };

        policy.Apply("No quest active", true, finished: true, idle: true);

        Assert.Equal(expected, string.Join(',', effects));
    }

    [Theory]
    [InlineData("Skip", true, true, true, "notify,/after-stop")]
    [InlineData("Skip", true, true, false, "notify,/after-stop,/logout")]
    [InlineData("Skip", false, false, true, "notify")]
    [InlineData("UI stop", true, false, true, "")]
    public void ListRun_HoldsFinalAction(string label, bool finished, bool expectedAsk, bool moreCharacters,
        string expected)
    {
        var configuration = Config();
        configuration.Stop.RunAfterProblemStop = false;
        configuration.Stop.EndAction = Configuration.EEndAction.LogOut;
        List<string> effects = [];
        List<bool> asked = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance)
        {
            StopHandler = canContinue =>
            {
                asked.Add(canContinue);
                return canContinue && moreCharacters;
            },
        };

        policy.Apply(label, true, finished);

        Assert.Equal([expectedAsk], asked);
        Assert.Equal(expected, string.Join(',', effects));
    }

    private static (QuestStopPolicy Policy, List<string> Effects) Ending(Configuration configuration,
        Func<bool>? isTraveling = null)
    {
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance, isTraveling: isTraveling);
        return (policy, effects);
    }

    [Fact]
    public void Destination_WaitsForArrival()
    {
        var configuration = Config();
        configuration.Stop.Destination = Configuration.EDestination.Inn;
        configuration.Stop.EndAction = Configuration.EEndAction.LogOut;
        bool traveling = true;
        var (policy, effects) = Ending(configuration, () => traveling);

        policy.Apply("Skip", true, finished: true);
        policy.Update(DateTime.Now.AddSeconds(10));
        Assert.Equal(["notify", "/li inn"], effects);

        traveling = false;
        policy.Update(DateTime.Now.AddSeconds(10));
        Assert.Equal(["notify", "/li inn", "/after-stop", "/logout"], effects);
        Assert.False(policy.IsEnding);
    }

    [Fact]
    public void NoLifestream_SkipsTravel()
    {
        var configuration = Config();
        configuration.Stop.Destination = Configuration.EDestination.Inn;
        configuration.Stop.EndAction = Configuration.EEndAction.LogOut;
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance, hasLifestream: () => false);

        policy.Apply("Skip", true, finished: true);
        policy.Update(DateTime.Now.AddSeconds(10));

        Assert.Equal(["notify", "/after-stop", "/logout"], effects);
    }

    [Fact]
    public void Wait_HoldsFinalAction()
    {
        var configuration = Config();
        configuration.Stop.WaitSeconds = 30;
        configuration.Stop.EndAction = Configuration.EEndAction.LogOut;
        var (policy, effects) = Ending(configuration);

        policy.Apply("Skip", true, finished: true);
        policy.Update(DateTime.Now.AddSeconds(10));
        Assert.Equal(["notify", "/after-stop"], effects);

        policy.Update(DateTime.Now.AddSeconds(31));
        Assert.Equal(["notify", "/after-stop", "/logout"], effects);
    }

    [Fact]
    public void Wait_SkippedWhenNothingFollows()
    {
        var configuration = Config();
        configuration.Stop.WaitSeconds = 30;
        var (policy, _) = Ending(configuration);

        policy.Apply("Skip", true, finished: true);

        Assert.False(policy.IsEnding);
    }

    [Fact]
    public void CancelEnding_DropsFinalAction()
    {
        var configuration = Config();
        configuration.Stop.WaitSeconds = 30;
        configuration.Stop.EndAction = Configuration.EEndAction.CloseGame;
        var (policy, effects) = Ending(configuration);

        policy.Apply("Skip", true, finished: true);
        policy.CancelEnding();
        policy.Update(DateTime.Now.AddSeconds(60));

        Assert.Equal(["notify", "/after-stop"], effects);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "/li")]
    [InlineData(2, "/li auto")]
    [InlineData(3, "/li inn")]
    [InlineData(6, "/li apt")]
    public void Destination_MapsToLifestream(int destination, string? expected) =>
        Assert.Equal(expected, QuestStopPolicy.GetCommand((Configuration.EDestination)destination));

    [Fact]
    public void Commands_RunOnePerLine()
    {
        var configuration = Config();
        configuration.Stop.CommandAfterStop = "/one\r\n\n  /two arg  \nthree\n/four";

        Assert.Equal(["notify", "/one", "/two arg", "/four"], Apply(configuration, finished: true));
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(1, null, "/logout")]
    [InlineData(2, null, "/shutdown")]
    [InlineData(1, 0, null)]
    [InlineData(0, 2, "/shutdown")]
    public void EndAction_PickBeatsSetting(int saved, int? picked, string? expected)
    {
        var configuration = Config();
        configuration.Stop.RunCommandAfterStop = false;
        configuration.Stop.EndAction = (Configuration.EEndAction)saved;

        Assert.Equal(expected == null ? ["notify"] : ["notify", expected],
            Apply(configuration, finished: true, (Configuration.EEndAction?)picked));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Run_UsesGameWhenNoPlugin(bool pluginHandles, bool expectGame)
    {
        List<string> game = [];

        QuestStopPolicy.Run("/logout", _ => pluginHandles, game.Add);

        Assert.Equal(expectGame ? ["/logout"] : [], game);
    }

    private static List<string> Apply(Configuration configuration, bool finished,
        Configuration.EEndAction? picked = null)
    {
        List<string> effects = [];
        var policy = new QuestStopPolicy(configuration, _ => effects.Add("notify"), _ => { }, effects.Add,
            Substitute.For<IChatGui>(), NullLogger<QuestStopPolicy>.Instance);
        policy.Apply("Skip", true, finished, picked);
        return effects;
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
        reports.Policy.ReportStopAfterAttune(EAetheryteLocation.Limsa);

        Assert.Equal(["Reached or exceeded target level 50.", "Completed quest 'Test', stopping as requested.",
            "Stopping before traveling to another zone, as requested.", "Attuned to the aetheryte, stopping as requested."], reports.Printed);
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
        configuration.Stop.RunAfterProblemStop = true;
        configuration.Stop.CommandAfterStop = "/after-stop";
        return configuration;
    }
}
