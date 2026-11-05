using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class FishOperationTests : IDisposable
{
    private readonly TestClock _clock = new();
    private readonly IFramework _framework = Substitute.For<IFramework>();
    private readonly ILogger<FeeshIpc> _logger = Substitute.For<ILogger<FeeshIpc>>();
    private readonly FishingOperationState _operation;
    private readonly FeeshIpc _ipc;
    private readonly Fish.DoFish _executor;
    private readonly TaskQueue _queue = new(NullLogger.Instance);
    private readonly Fish.FishTask _task = new(QuestTestData.CreateQuest(new QuestId(65000)),
        new GatheredItem { ItemId = 42, ItemCount = 3 }, []);
    private string? _catching = "";
    private bool _available = true;
    private bool? _busy = false;
    private bool _canCatch = true;
    private bool _catchResult = true;
    private bool _catchThrows;
    private bool _cancelAccepted = true;
    private bool _releaseThrows;
    private bool _objectives;
    private int _eligible;
    private readonly List<(string Owner, uint Fish, int Count, bool Keep)> _catches = [];
    private readonly List<string> _cancels = [];
    private readonly List<uint> _releases = [];
    private readonly HashSet<uint> _holds = [];

    public FishOperationTests()
    {
        _operation = new FishingOperationState(_clock);
        _framework.IsInFrameworkUpdateThread.Returns(true);
        _ipc = new FeeshIpc(_logger, _operation, _framework,
            () => _busy ?? throw new InvalidOperationException("unloaded"),
            _ => _canCatch ? "stand" : "",
            (owner, fish, count, keep) =>
            {
                _catches.Add((owner, fish, count, keep));
                if (keep && (_catchResult || _catchThrows))
                    _holds.Add(fish);
                if (_catchThrows)
                    throw new InvalidOperationException("reply lost after acceptance");
                return _catchResult;
            },
            () => _available && _catching != null ? _catching : throw new InvalidOperationException("unloaded"),
            owner => { _cancels.Add(owner); return _cancelAccepted; },
            (_, fish) =>
            {
                _releases.Add(fish);
                if (_releaseThrows)
                    throw new InvalidOperationException("release unavailable");
                return _holds.Remove(fish);
            }, _clock);
        _executor = new Fish.DoFish(_ipc, _operation, NullLogger<Fish.DoFish>.Instance,
            _ => _objectives, _ => _eligible, id => $"fish {id}", _clock);
    }

    [Fact]
    public void OneSizeFitsAll_FisherBranch_RequestsSunshellFromFeesh()
    {
        var quest = QuestTestData.LoadQuest(new QuestId(3097), new EmbeddedQuest(
            "QuestPaths/4.x - Stormblood/Allied Societies/Namazu/Story/3097_One Size Fits All.json"));
        var sequence = quest.FindSequence(255)!;
        var step = sequence.Steps.Single(x => x.RequiredQuestAcceptedJob.Contains(EExtendedClassJob.Fisher));

        Assert.Empty(new Gather.Factory().CreateAllTasks(quest, sequence, step));
        var task = Assert.IsType<Fish.FishTask>(Assert.Single(new Fish.Factory().CreateAllTasks(quest, sequence, step)));
        Assert.True(ProviderTravel.IsHandled(sequence, step, [new FeeshOperationProvider(_ipc, _operation)]));
        Assert.True(_queue.StartTask(_executor, task));
        Assert.Equal((FeeshIpc.Owner, 22668u, 1, true), Assert.Single(_catches));
    }

    [Fact]
    public void SatisfiedObjective_SkipsUnavailableFeesh()
    {
        _available = false;
        _objectives = true;
        Assert.False(_queue.StartTask(_executor, _task));
        Assert.Empty(_catches);
    }

    [Fact]
    public void CollectableFish_StopsForManualFishingWithoutContactingFeesh()
    {
        var task = _task with { GatheredItem = new GatheredItem { ItemId = 42, ItemCount = 6, Collectability = 433 } };
        var e = Assert.Throws<TaskException>(() => _queue.StartTask(_executor, task));
        Assert.Contains("collectable", e.Message);
        Assert.Contains("fish 42", e.Message);
        Assert.Empty(_catches);
    }

    [Fact]
    public void FishWithoutKnownSpot_StopsForManualFishing()
    {
        _canCatch = false;
        var e = Assert.Throws<TaskException>(() => _queue.StartTask(_executor, _task));
        Assert.Contains("doesn't know where", e.Message);
        Assert.Empty(_catches);
    }

    [Fact]
    public void MissingWahTools_StopsForManualFishing()
    {
        _available = false;
        var e = Assert.Throws<TaskException>(() => _queue.StartTask(_executor, _task));
        Assert.Contains("WahTools", e.Message);
        Assert.Empty(_catches);
    }

    [Fact]
    public void FlagOnlyStep_StopsForManualFishing()
    {
        var task = new Fish.FishTask(_task.Quest, null,
            [null, new QuestWorkValue(1, 1, EQuestWorkMode.Bitwise), null, null, null, null]);
        Assert.Throws<TaskException>(() => _queue.StartTask(_executor, task));
        Assert.Empty(_catches);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void BusyOrUnreadableWahTools_IsRefusedBeforeSubmitting(bool? busy)
    {
        _busy = busy;
        var e = Assert.Throws<TaskException>(Start);
        Assert.Contains("busy", e.Message);
        Assert.Empty(_catches);
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void Start_RequestsOnlyTheMissingCountAndKeepsIt()
    {
        _eligible = 1;
        Start();
        Assert.Equal((FeeshIpc.Owner, 42u, 2, true), Assert.Single(_catches));
        Assert.True(_operation.HasOperation);
        Assert.False(_operation.RequiresReconciliation);
    }

    [Fact]
    public void RefusedCatch_ReleasesSubmissionWithoutCancelling()
    {
        _catchResult = false;
        var e = Assert.Throws<TaskException>(Start);
        Assert.Contains("refused", e.Message);
        Assert.False(_operation.HasOperation);
        Assert.Empty(_cancels);
    }

    [Fact]
    public void LostReply_RetainsOwnershipAndCancelsOnlyOurJob()
    {
        _catchThrows = true;
        Assert.Throws<TaskException>(Start);
        Assert.True(_operation.HasOperation);
        Assert.True(_operation.IsAbandoned);
        Assert.True(_operation.RequiresReconciliation);
        Assert.Equal(FeeshIpc.Owner, Assert.Single(_cancels));
        Assert.Equal(42u, Assert.Single(_releases));
    }

    [Fact]
    public void CatchDone_WithObjectivesCompletesAndReleasesTheHold()
    {
        Start();
        _catching = "fishing 0/3";
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        Assert.True(_operation.HasStarted);

        _catching = "done 3/3";
        _objectives = true;
        _ipc.OnCatchDone(FeeshIpc.Owner, true);
        Assert.Equal(ETaskResult.TaskComplete, UpdateExecutor());
        Assert.False(_operation.HasOperation);
        Assert.Equal(42u, Assert.Single(_releases));
    }

    [Fact]
    public void FailedRelease_RetriesOnFrameworkWithoutJobOwnershipAndIsThrottled()
    {
        CompleteWithFailedRelease();
        Assert.False(_operation.HasOperation);
        Assert.Contains(42u, _holds);
        FrameworkUpdate();
        _ipc.CancelOwned();
        Assert.Single(_releases);

        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        Assert.Equal(2, _releases.Count);
        Assert.Contains(42u, _holds);

        _releaseThrows = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        Assert.Equal(3, _releases.Count);
        Assert.Empty(_holds);
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        Assert.Equal(3, _releases.Count);
        Assert.Empty(_cancels);
    }

    [Fact]
    public void FailedRelease_SurvivesStartingAnotherFish()
    {
        CompleteWithFailedRelease();
        Assert.Equal(FeeshStartResult.Started, _ipc.StartCatch(43, 1, out _));
        _releaseThrows = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();

        Assert.Equal(new uint[] { 42, 42 }, _releases);
        Assert.Equal(43u, Assert.Single(_holds));
        Assert.True(_operation.HasOperation);
        Assert.Empty(_cancels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameFishReplacement_SupersedesOldReleaseEvenWhenStartReplyIsLost(bool lostReply)
    {
        CompleteWithFailedRelease();
        _catchThrows = lostReply;
        Assert.Equal(lostReply ? FeeshStartResult.Uncertain : FeeshStartResult.Started,
            _ipc.StartCatch(42, 1, out _));
        _releaseThrows = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        Assert.Single(_releases);
        Assert.Contains(42u, _holds);

        _ipc.CancelOwned();
        Assert.Equal(2, _releases.Count);
        Assert.Empty(_holds);
    }

    [Fact]
    public void RefusedSameFishCatch_KeepsTheOldReleasePending()
    {
        CompleteWithFailedRelease();
        _catchResult = false;
        Assert.Equal(FeeshStartResult.Refused, _ipc.StartCatch(42, 1, out _));
        _releaseThrows = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        Assert.Equal(2, _releases.Count);
        Assert.Empty(_holds);
    }

    [Fact]
    public void AlreadyReleasedHold_CompletesPendingCleanup()
    {
        CompleteWithFailedRelease();
        _holds.Clear(); // Released manually in WahTools; ReleaseCatch now returns false.
        _releaseThrows = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        Assert.Equal(2, _releases.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispose_RetriesPendingReleaseAndUnsubscribesFrameworkUpdates(bool releaseStillFails)
    {
        CompleteWithFailedRelease();
        _releaseThrows = releaseStillFails;
        _ipc.Dispose();
        Assert.Equal(2, _releases.Count);
        Assert.Equal(releaseStillFails, _holds.Contains(42));
        if (releaseStillFails)
            Assert.Contains("42", Assert.Single(DisposalWarnings()));
        else
            Assert.Empty(DisposalWarnings());
        _releaseThrows = false;
        _clock.Advance(TimeSpan.FromSeconds(2));
        FrameworkUpdate();
        Assert.Equal(2, _releases.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Dispose_WhenFrameworkIsUnloading_ReportsAllRemainingHoldsWithoutIpc(bool pending, bool active)
    {
        if (pending)
            CompleteWithFailedRelease();
        if (active)
            Assert.Equal(FeeshStartResult.Started, _ipc.StartCatch(43, 1, out _));

        _releaseThrows = false;
        _framework.IsInFrameworkUpdateThread.Returns(false);
        _framework.IsFrameworkUnloading.Returns(true);
        int releaseAttempts = _releases.Count;
        _framework.ClearReceivedCalls();
        _ipc.Dispose();

        Assert.Equal(releaseAttempts, _releases.Count);
        Assert.Empty(_cancels);
        Assert.DoesNotContain(_framework.ReceivedCalls(), call => call.GetMethodInfo().Name == "RunOnFrameworkThread");
        if (!pending && !active)
            Assert.Empty(DisposalWarnings());
        else
        {
            string warning = Assert.Single(DisposalWarnings());
            Assert.Equal(pending, warning.Contains("42", StringComparison.Ordinal));
            Assert.Equal(active, warning.Contains("43", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EndedStateBeforeTheJobRuns_IsTreatedAsThePreviousJob()
    {
        Start();
        _catching = "done";
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        Assert.False(_operation.IsTerminal);
        Assert.False(_operation.HasStarted);
    }

    [Fact]
    public void OtherOwnersCatchDone_IsIgnored()
    {
        Start();
        _catching = "fishing 1/3";
        _ipc.OnCatchDone("SomeoneElse", true);
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        Assert.False(_operation.IsTerminal);
    }

    [Fact]
    public void MissedCatchDone_StableWahToolsIdleEndsTheJob()
    {
        Start();
        _catching = "fishing 2/3";
        _busy = true;
        UpdateExecutor();
        _catching = "done 3/3";
        _busy = false;
        _objectives = true;
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        for (int i = 0; i < 4; ++i)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _ipc.PollOperation();
        }

        Assert.Equal(ETaskResult.TaskComplete, UpdateExecutor());
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void PartialCatch_FailsAndReleasesTheHold()
    {
        Start();
        _catching = "partial 1/3: out of bait";
        _ipc.OnCatchDone(FeeshIpc.Owner, false);
        var e = Assert.Throws<TaskException>(() => UpdateExecutor());
        Assert.Contains("out of bait", e.Message);
        Assert.False(_operation.HasOperation);
        Assert.Equal(42u, Assert.Single(_releases));
    }

    [Fact]
    public void SucceededWithoutObjectives_FailsAfterInventoryGracePeriod()
    {
        Start();
        _catching = "done 3/3";
        _ipc.OnCatchDone(FeeshIpc.Owner, true);
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Throws<TaskException>(() => UpdateExecutor());
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void ObjectivesMetEarly_CancelsOurJobAndCountsTheCancelAsDone()
    {
        Start();
        _catching = "fishing 1/3";
        UpdateExecutor();
        _objectives = true;
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        Assert.False(_operation.IsAbandoned);
        Assert.Equal(FeeshIpc.Owner, Assert.Single(_cancels));

        _catching = "cancelled";
        _ipc.OnCatchDone(FeeshIpc.Owner, false);
        Assert.Equal(ETaskResult.TaskComplete, UpdateExecutor());
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void RefusedCancel_IsRetriedWithoutAbandoning()
    {
        Start();
        _cancelAccepted = false;
        _objectives = true;
        UpdateExecutor();
        UpdateExecutor();
        Assert.Single(_cancels);
        _clock.Advance(TimeSpan.FromSeconds(2));
        UpdateExecutor();
        Assert.Equal(2, _cancels.Count);
        Assert.False(_operation.IsAbandoned);
    }

    [Fact]
    public void Stop_AbandonsReleasesAndClearsOnceFeeshAcknowledges()
    {
        Start();
        _catching = "fishing 1/3";
        UpdateExecutor();
        _queue.Reset();
        Assert.True(_operation.IsAbandoned);
        Assert.Equal(FeeshIpc.Owner, Assert.Single(_cancels));
        Assert.Equal(42u, Assert.Single(_releases));
        Assert.True(_operation.HasOperation);

        _catching = "cancelled";
        _ipc.OnCatchDone(FeeshIpc.Owner, false);
        _ipc.PollOperation();
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void ProviderLoss_RequiresReconciliationAndIsNeverIdle()
    {
        Start();
        _catching = "fishing 1/3";
        UpdateExecutor();
        _available = false;
        _busy = null;
        _ipc.PollOperation();
        _clock.Advance(TimeSpan.FromSeconds(10));
        _ipc.PollOperation();
        Assert.True(_operation.RequiresReconciliation);
        Assert.Throws<TaskException>(() => _executor.Update());
        Assert.False(_operation.TryConfirmIdle());
    }

    [Fact]
    public void UnrecognisedStatus_IsNeverTreatedAsDone()
    {
        Start();
        _catching = "fishing 1/3";
        UpdateExecutor();
        _catching = "mooching";
        _objectives = true;
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        Assert.False(_operation.IsTerminal);
    }

    [Fact]
    public void JobThatNeverStarts_TimesOutAndCancels()
    {
        Start();
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Throws<TaskException>(() => UpdateExecutor());
        Assert.True(_operation.RequiresReconciliation);
        Assert.Equal(FeeshIpc.Owner, Assert.Single(_cancels));
        Assert.Single(_catches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("done previous fish 3/3")]
    [InlineData("cancelled previous fish 1/3")]
    public void UnobservedInactiveJob_CanBeConfirmedOnlyAfterTimeoutAndStableIdle(string status)
    {
        Start();
        _catching = status;
        _cancelAccepted = false;
        _ipc.PollOperation();
        Assert.False(_operation.TryConfirmIdle());

        _clock.Advance(TimeSpan.FromSeconds(30));
        _ipc.PollOperation();
        Assert.Equal(ExternalOperationStatus.Unknown, _operation.Status);
        Assert.True(_operation.RequiresReconciliation);
        Assert.False(_operation.TryConfirmIdle());
        Assert.Single(_cancels);
        _queue.Reset(); // Match the controller's stop before manual reconciliation.
        for (int i = 0; i < 4; ++i)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _ipc.PollOperation();
        }

        Assert.False(_operation.IsTerminal);
        Assert.True(_operation.TryConfirmIdle());
        Assert.False(_operation.HasOperation);
        Assert.Single(_catches);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void TimedOutJob_BusyOrUnreadableProviderCannotBeConfirmed(bool? busy)
    {
        Start();
        _clock.Advance(TimeSpan.FromSeconds(30));
        _busy = busy;
        for (int i = 0; i < 6; ++i)
        {
            _ipc.PollOperation();
            _clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(ExternalOperationStatus.Unknown, _operation.Status);
        Assert.True(_operation.RequiresReconciliation);
        Assert.False(_operation.TryConfirmIdle());
    }

    [Theory]
    [InlineData("fishing fish 0/3")]
    [InlineData("finishing fish 3/3")]
    public void ActiveStateAfterStartupTimeout_PreventsIdleConfirmation(string status)
    {
        Start();
        _clock.Advance(TimeSpan.FromSeconds(30));
        _ipc.PollOperation();
        for (int i = 0; i < 4; ++i)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _ipc.PollOperation();
        }
        Assert.True(_operation.CanConfirmIdle);

        _catching = status;
        _ipc.PollOperation();
        Assert.True(_operation.HasStarted);
        Assert.Equal(ExternalOperationStatus.CancellationRequested, _operation.Status);
        Assert.False(_operation.TryConfirmIdle());
    }

    [Theory]
    [InlineData(null, nameof(EFeeshJobState.Unavailable))]
    [InlineData("", nameof(EFeeshJobState.None))]
    [InlineData("  ", nameof(EFeeshJobState.None))]
    // Real formats, from WahTools FeeshModule.Control.cs.
    [InlineData("fishing Harbor Herring 0/3", nameof(EFeeshJobState.Fishing))]
    [InlineData("finishing Harbor Herring 3/3", nameof(EFeeshJobState.Finishing))]
    [InlineData("done Harbor Herring 3/3", nameof(EFeeshJobState.Done))]
    [InlineData("partial Harbor Herring 1/3: Harbor Herring: no bait", nameof(EFeeshJobState.Partial))]
    [InlineData("failed Harbor Herring 0/3: tour stopped", nameof(EFeeshJobState.Failed))]
    [InlineData("cancelled Harbor Herring 1/3: Questionable called it off", nameof(EFeeshJobState.Cancelled))]
    [InlineData("stopped Harbor Herring 1/3: stopped by hand", nameof(EFeeshJobState.Stopped))]
    [InlineData("3/3 done", nameof(EFeeshJobState.Unrecognized))]
    [InlineData("mooching", nameof(EFeeshJobState.Unrecognized))]
    public void CatchingText_IsParsedByItsFirstWord(string? text, string expected) =>
        Assert.Equal(expected, FeeshIpc.ParseState(text).ToString());

    [Fact]
    public void CollectabilityRequirement_QueriesOnlyQualifiedCollectableInventory()
    {
        List<(uint, bool, short)> queries = [];
        var item = new GatheredItem { ItemId = 42, Collectability = 350 };
        int count = Fish.CountEligibleItems(item, (id, hq, minimum) => { queries.Add((id, hq, minimum)); return 2; });
        Assert.Equal(2, count);
        Assert.Equal((500042u, false, (short)350), Assert.Single(queries));
    }

    [Fact]
    public void UnqualifiedRequirement_CountsNormalHighQualityAndCollectableStacks()
    {
        List<(uint, bool, short)> queries = [];
        int count = Fish.CountEligibleItems(new GatheredItem { ItemId = 42 },
            (id, hq, minimum) => { queries.Add((id, hq, minimum)); return 2; });
        Assert.Equal(6, count);
        Assert.Equal(new[] { (42u, false, (short)0), (42u, true, (short)0), (500042u, false, (short)0) }, queries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuestFlags_AreAuthoritativeWithOrWithoutAnInventoryTarget(bool hasItem)
    {
        // Construct afresh: completion-flag presence is cached by the record constructor.
        var task = new Fish.FishTask(_task.Quest, hasItem ? _task.GatheredItem : null,
            [null, new QuestWorkValue(1, 1, EQuestWorkMode.Bitwise), null, null, null, null]);
        Assert.True(Fish.ObjectivesSatisfied(task, true, 0));
        Assert.False(Fish.ObjectivesSatisfied(task, false, 0));
    }

    public void Dispose() => _ipc.Dispose();

    private IEnumerable<string> DisposalWarnings() => _logger.ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == "Log" && call.GetArguments()[0] is LogLevel.Warning)
        .Select(call => call.GetArguments()[2]?.ToString() ?? "");

    private void FrameworkUpdate() => _framework.Update += Raise.Event<IFramework.OnUpdateDelegate>(_framework);

    private void CompleteWithFailedRelease()
    {
        Start();
        _objectives = true;
        _catching = "done fish 3/3";
        _releaseThrows = true;
        _ipc.OnCatchDone(FeeshIpc.Owner, true);
        Assert.Equal(ETaskResult.TaskComplete, UpdateExecutor());
        Assert.True(_queue.Reset());
        Assert.Single(_releases);
    }

    private ETaskResult UpdateExecutor()
    {
        _ipc.PollOperation();
        return _executor.Update();
    }

    private void Start() => Assert.True(_queue.StartTask(_executor, _task));
}
