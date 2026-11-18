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
        }, () => Task.FromResult<ReportApi.StatusReply?>(null), () => character,
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
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "QuestPaths", "quest-v2.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(directory.FullName, "QuestPaths"), "*.json",
                     SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith("quest-v2", StringComparison.Ordinal) ||
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

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static ReportApi.StatusReply Reply(Quest quest, bool problem = false)
    {
        long at = Now.ToUnixTimeMilliseconds();
        return new(at, new()
        {
            [quest.Id.ToString()] = new()
            {
                [QuestReporter.Fingerprint(quest.Root)] = new(5, problem ? 6 : 5, 5, problem ? 1 : 0,
                    at, at, at, problem ? at : null, null)
            }
        });
    }

    private QuestReporter StatusReporter(Func<Task<ReportApi.StatusReply?>> fetch,
        Func<DateTimeOffset>? now = null) => new(_configuration, _ => Task.FromResult(true), fetch,
        () => "12345", NullLogger.Instance, now: now ?? (() => Now));

    [Fact]
    public async Task Status_UsesExactCurrentPath()
    {
        Quest quest = MakeQuest();
        QuestReporter reporter = StatusReporter(() => Task.FromResult<ReportApi.StatusReply?>(Reply(quest)));
        await reporter.RefreshStatusAsync();
        Assert.Equal(EReportState.Working, reporter.StatusFor(quest).State);
        Assert.Equal(EReportReason.NoReports, reporter.StatusFor(MakeQuest(dataId: 2)).Reason);
        Assert.True(reporter.StatusFor(MakeQuest(dataId: 2)).NeedsAttention);
    }

    [Fact]
    public async Task EmptyReply_IsKnownUnreported()
    {
        QuestReporter reporter = StatusReporter(() => Task.FromResult<ReportApi.StatusReply?>(new(Now.ToUnixTimeMilliseconds(), [])));
        await reporter.RefreshStatusAsync();
        Assert.Equal(EReportReason.NoReports, reporter.StatusFor(MakeQuest()).Reason);
        Assert.True(reporter.StatusFor(MakeQuest()).NeedsAttention);
    }

    [Fact]
    public async Task FailedRefresh_DropsOldGreen()
    {
        Quest quest = MakeQuest();
        DateTimeOffset now = Now;
        int calls = 0;
        QuestReporter reporter = StatusReporter(() => Task.FromResult(++calls == 1 ? Reply(quest) : null), () => now);
        await reporter.RefreshStatusAsync();
        Assert.Equal(EReportState.Working, reporter.StatusFor(quest).State);

        now = now.AddHours(1);
        await reporter.RefreshStatusAsync();
        ReportStatus status = reporter.StatusFor(quest);
        Assert.Equal(EReportReason.Unavailable, status.Reason);
        Assert.False(status.NeedsAttention);
        Assert.Null(status.Reports);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedRefresh_RetriesAfterMinute()
    {
        DateTimeOffset now = Now;
        int calls = 0;
        QuestReporter reporter = StatusReporter(() =>
        {
            ++calls;
            return Task.FromResult<ReportApi.StatusReply?>(null);
        }, () => now);
        await reporter.RefreshStatusAsync();
        await reporter.RefreshStatusAsync();
        Assert.Equal(1, calls);
        now = now.AddMinutes(1);
        await reporter.RefreshStatusAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Loading_DoesNotQueueEverything()
    {
        var pending = new TaskCompletionSource<ReportApi.StatusReply?>(TaskCreationOptions.RunContinuationsAsynchronously);
        QuestReporter reporter = StatusReporter(() => pending.Task);
        Task fetch = reporter.RefreshStatusAsync();
        ReportStatus status = reporter.StatusFor(MakeQuest());
        Assert.Equal(EReportReason.Loading, status.Reason);
        Assert.False(status.NeedsAttention);
        pending.SetResult(new(Now.ToUnixTimeMilliseconds(), []));
        await fetch;
    }

    [Fact]
    public async Task Refetch_KeepsDataUntilItLands()
    {
        Quest quest = MakeQuest();
        var pending = new TaskCompletionSource<ReportApi.StatusReply?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        QuestReporter reporter = StatusReporter(() => ++calls == 1 ? Task.FromResult<ReportApi.StatusReply?>(Reply(quest)) : pending.Task);
        await reporter.RefreshStatusAsync();
        reporter.Invalidate();
        Task refetch = reporter.RefreshStatusAsync();
        Assert.Equal(EReportState.Working, reporter.StatusFor(quest).State);
        pending.SetResult(null);
        await refetch;
        Assert.Equal(EReportReason.Unavailable, reporter.StatusFor(quest).Reason);
    }

    [Fact]
    public async Task Disabled_DoesNotFetchOrQueue()
    {
        _configuration.Reports.ShowCounts = false;
        int calls = 0;
        QuestReporter reporter = StatusReporter(() =>
        {
            ++calls;
            return Task.FromResult<ReportApi.StatusReply?>(Reply(MakeQuest()));
        });
        await reporter.RefreshStatusAsync();
        Assert.Equal(EReportReason.Disabled, reporter.StatusFor(MakeQuest()).Reason);
        Assert.False(reporter.StatusFor(MakeQuest()).NeedsAttention);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Invalidate_IgnoresOlderResponse()
    {
        var first = new TaskCompletionSource<ReportApi.StatusReply?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ReportApi.StatusReply?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        QuestReporter reporter = StatusReporter(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                return first.Task;
            }
            return second.Task;
        });
        Task oldFetch = reporter.RefreshStatusAsync();
        await started.Task;
        reporter.Invalidate();
        Task newFetch = reporter.RefreshStatusAsync();
        second.SetResult(new(Now.ToUnixTimeMilliseconds(), []));
        await newFetch;
        first.SetResult(Reply(MakeQuest()));
        await oldFetch;
        Assert.Equal(EReportReason.NoReports, reporter.StatusFor(MakeQuest()).Reason);
    }

    [Fact]
    public async Task BadReply_IsUnavailable()
    {
        QuestReporter reporter = StatusReporter(() => Task.FromResult<ReportApi.StatusReply?>(new(0, [])));
        await reporter.RefreshStatusAsync();
        Assert.Equal(EReportReason.Unavailable, reporter.StatusFor(MakeQuest()).Reason);
        Assert.False(reporter.StatusFor(MakeQuest()).NeedsAttention);
    }

    [Fact]
    public async Task Invalidate_FetchesFreshUntilSuccess()
    {
        List<bool> requests = [];
        DateTimeOffset now = Now;
        var reporter = new QuestReporter(_configuration, _ => Task.FromResult(true), fresh =>
        {
            requests.Add(fresh);
            return Task.FromResult<ReportApi.StatusReply?>(requests.Count == 2 ? null :
                Reply(MakeQuest()) with { At = now.ToUnixTimeMilliseconds() });
        }, () => "12345", NullLogger.Instance, now: () => now);
        await reporter.RefreshStatusAsync();
        reporter.Invalidate();
        await reporter.RefreshStatusAsync();
        now = now.AddMinutes(1);
        await reporter.RefreshStatusAsync();
        now = now.AddHours(1);
        await reporter.RefreshStatusAsync();
        Assert.Equal([false, true, true, false], requests);
    }

    [Fact]
    public async Task Invalidate_DoesNotDowngradeFresh()
    {
        List<bool> requests = [];
        DateTimeOffset now = Now;
        var reporter = new QuestReporter(_configuration, _ => Task.FromResult(true), fresh =>
        {
            requests.Add(fresh);
            return Task.FromResult<ReportApi.StatusReply?>(Reply(MakeQuest()) with { At = now.ToUnixTimeMilliseconds() });
        }, () => "12345", NullLogger.Instance, now: () => now);
        reporter.Invalidate();
        reporter.Invalidate(fresh: false);
        await reporter.RefreshStatusAsync();
        now = now.AddMinutes(16);
        reporter.Invalidate(fresh: false);
        await reporter.RefreshStatusAsync();
        Assert.Equal([true, false], requests);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData((int)EProblemKind.PlayerStopped, false)]
    [InlineData((int)EProblemKind.Stuck, true)]
    public async Task OwnReport_FreshOnlyForProblem(int? kind, bool expected)
    {
        bool? freshRequested = null;
        var reporter = new QuestReporter(_configuration, _ => Task.FromResult(true), fresh =>
        {
            freshRequested = fresh;
            return Task.FromResult<ReportApi.StatusReply?>(Reply(MakeQuest()));
        }, () => "12345", NullLogger.Instance, now: () => Now);
        reporter.SendManual(new(MakeQuest()), kind is { } number ? (EProblemKind)number : null, []);
        await reporter.RefreshStatusAsync();
        Assert.Equal(expected, freshRequested);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(0)]
    public async Task OldCache_RefetchesOnceFresh(int age)
    {
        Quest quest = MakeQuest();
        DateTimeOffset now = Now;
        ReportApi.StatusReply newer = Reply(quest, problem: true);
        ReportApi.StatusReply older = Reply(quest) with { At = newer.At + age };
        List<bool> requests = [];
        var reporter = new QuestReporter(_configuration, _ => Task.FromResult(true), fresh =>
        {
            requests.Add(fresh);
            return Task.FromResult<ReportApi.StatusReply?>(requests.Count == 2 ? older : newer);
        }, () => "12345", NullLogger.Instance, now: () => now);
        reporter.Invalidate();
        await reporter.RefreshStatusAsync();
        now = now.AddMinutes(16);
        reporter.Invalidate(fresh: false);
        await reporter.RefreshStatusAsync();
        Assert.Equal([true, false, true], requests);
        Assert.Equal(EReportState.Problems, reporter.StatusFor(quest).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderFresh_DoesNotRegress(bool failure)
    {
        Quest quest = MakeQuest();
        DateTimeOffset now = Now;
        ReportApi.StatusReply newer = Reply(quest, problem: true);
        ReportApi.StatusReply older = Reply(quest) with { At = newer.At - 1000 };
        List<bool> requests = [];
        var reporter = new QuestReporter(_configuration, _ => Task.FromResult(true), fresh =>
        {
            requests.Add(fresh);
            return Task.FromResult<ReportApi.StatusReply?>(requests.Count == 1 ? newer :
                requests.Count == 3 && failure ? null : older);
        }, () => "12345", NullLogger.Instance, now: () => now);
        reporter.Invalidate();
        await reporter.RefreshStatusAsync();
        now = now.AddMinutes(16);
        reporter.Invalidate(fresh: false);
        await reporter.RefreshStatusAsync();
        Assert.Equal([true, false, true], requests);
        Assert.Equal(EReportReason.Unavailable, reporter.StatusFor(quest).Reason);
        Assert.False(reporter.StatusFor(quest).NeedsAttention);
    }

    [Fact]
    public async Task FreshAction_IgnoresAheadCache()
    {
        Quest quest = MakeQuest();
        DateTimeOffset now = Now;
        ReportApi.StatusReply current = Reply(quest, problem: true);
        ReportApi.StatusReply aheadCache = Reply(quest) with { At = current.At + 60000 };
        List<bool> requests = [];
        var reporter = new QuestReporter(_configuration, _ => Task.FromResult(true), fresh =>
        {
            requests.Add(fresh);
            return Task.FromResult<ReportApi.StatusReply?>(fresh ? current : aheadCache);
        }, () => "12345", NullLogger.Instance, now: () => now);
        reporter.Invalidate();
        await reporter.RefreshStatusAsync();
        now = now.AddMinutes(1);
        reporter.Invalidate(fresh: false);
        await reporter.RefreshStatusAsync();
        Assert.Equal([true, true], requests);
        Assert.Equal(EReportState.Problems, reporter.StatusFor(quest).State);
    }
}
