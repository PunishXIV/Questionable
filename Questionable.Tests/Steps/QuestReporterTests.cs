using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Questionable.Controller;
using Questionable.Domain;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using QuestProgress = Questionable.Controller.QuestController.QuestProgress;

namespace Questionable.Tests.Steps;

public sealed class QuestReporterTests
{
    private readonly Configuration _configuration = new();
    private readonly List<ReportApi.Body> _sent = [];

    private QuestReporter Reporter(string? character = "12345") =>
        new(_configuration, body =>
        {
            _sent.Add(body);
            return Task.FromResult(true);
        }, () => Task.FromResult<Dictionary<string, Dictionary<string, int[]>>?>(null), () => character,
            NullLogger.Instance);

    private static Quest MakeQuest(Quest.ESource source = Quest.ESource.DownloadedBundle, uint dataId = 1)
    {
        Quest quest = QuestTestData.CreateQuest(new QuestId(1190),
            QuestTestData.Seq(0, new QuestStep(EInteractionType.AcceptQuest, dataId, null, 132)));
        return new Quest { Id = quest.Id, Root = quest.Root, Info = quest.Info, Source = source };
    }

    [Fact]
    public void Fingerprint_FollowsThePath()
    {
        string first = QuestReporter.Fingerprint(MakeQuest().Root);

        Assert.Matches("^[0-9a-f]{16}$", first);
        Assert.Equal(first, QuestReporter.Fingerprint(MakeQuest().Root));
        Assert.NotEqual(first, QuestReporter.Fingerprint(MakeQuest(dataId: 2).Root));
    }

    [Fact]
    public void Fingerprint_EveryPublishedPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "QuestPaths", "quest-v1.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(directory.FullName, "QuestPaths"), "*.json",
                     SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith("quest-v1", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}schema", StringComparison.OrdinalIgnoreCase))
                continue;

            QuestRoot? root = JsonSerializer.Deserialize<QuestRoot>(File.ReadAllText(file));
            if (root == null)
                continue;
            Assert.Matches("^[0-9a-f]{16}$", QuestReporter.Fingerprint(root));
            ++count;
        }

        Assert.True(count > 1000, $"only {count} paths found");
    }

    [Fact]
    public void CharacterHash_HidesTheId()
    {
        string hash = QuestReporter.CharacterHash("12345");

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain("12345", hash, StringComparison.Ordinal);
    }

    // Quest.ESource is internal, so the cases pass its number.
    [Theory]
    [InlineData((int)Quest.ESource.DownloadedBundle, false, true)]
    [InlineData((int)Quest.ESource.Assembly, false, true)]
    [InlineData((int)Quest.ESource.ProjectDirectory, false, false)]
    [InlineData((int)Quest.ESource.ProjectDirectory, true, true)]
    [InlineData((int)Quest.ESource.UserDirectory, true, false)]
    public void CanReport_PublishedPathsOnly(int source, bool projectPaths, bool expected) =>
        Assert.Equal(expected, QuestReporter.CanReport((Quest.ESource)source, projectPaths));

    [Fact]
    public void Stop_SendsPendingProblem()
    {
        _configuration.Reports.Share = true;
        QuestReporter reporter = Reporter();

        reporter.Problem(EProblemKind.Stuck);
        reporter.Stopped(new QuestProgress(MakeQuest(), 2, 3), "Stuck after retries", ["MoveTo(1)", "Interact(2)"]);

        ReportApi.Body body = Assert.Single(_sent);
        Assert.Equal(("problem", "stuck", 2, 3), (body.Outcome, body.Kind, body.Sequence, body.Step));
        Assert.Equal(["MoveTo(1)", "Interact(2)"], body.Actions);
        Assert.NotNull(body.Versions);
    }

    [Fact]
    public void PlayerStop_SendsPlayerStopped()
    {
        _configuration.Reports.Share = true;

        Reporter().Stopped(new QuestProgress(MakeQuest()), QuestStopPolicy.UiStopReason, []);

        Assert.Equal("playerStopped", Assert.Single(_sent).Kind);
    }

    [Fact]
    public void OtherStop_SendsNothing()
    {
        _configuration.Reports.Share = true;

        Reporter().Stopped(new QuestProgress(MakeQuest()), "Quest replan blocked", []);

        Assert.Empty(_sent);
    }

    [Fact]
    public void NoConsent_SendsNothing()
    {
        QuestReporter reporter = Reporter();

        reporter.Problem(EProblemKind.Deaths);
        reporter.Stopped(new QuestProgress(MakeQuest()), "Too many deaths", []);
        reporter.Completed(MakeQuest());

        Assert.Empty(_sent);
    }

    [Fact]
    public void Completed_AfterProblem_NotWorked()
    {
        _configuration.Reports.Share = true;
        QuestReporter reporter = Reporter();
        Quest quest = MakeQuest();

        reporter.Problem(EProblemKind.QuestBattle);
        reporter.Stopped(new QuestProgress(quest), "Task end", []);
        reporter.Completed(quest);
        reporter.Completed(quest);

        Assert.Equal(["problem", "worked"], _sent.Select(x => x.Outcome));
        Assert.Null(_sent[1].Kind);
        Assert.Null(_sent[1].Actions);
    }

    [Fact]
    public void ProjectPath_NeedsTestingSwitch()
    {
        _configuration.Reports.Share = true;
        QuestReporter reporter = Reporter();
        Quest quest = MakeQuest(Quest.ESource.ProjectDirectory);

        reporter.Completed(quest);
        Assert.Empty(_sent);

        _configuration.Advanced.Debug = true;
        _configuration.Reports.ReportProjectPaths = true;
        reporter.Completed(quest);
        Assert.Single(_sent);
    }

    [Fact]
    public void Manual_SendsWithoutConsent()
    {
        Assert.True(Reporter().SendManual(new QuestProgress(MakeQuest(), 1, 0), EProblemKind.WrongTarget, []));

        Assert.Equal("wrongTarget", Assert.Single(_sent).Kind);
    }

    [Fact]
    public void LoggedOut_SendsNothing()
    {
        Assert.False(Reporter(character: null).SendManual(new QuestProgress(MakeQuest()), null, []));

        Assert.Empty(_sent);
    }
}
