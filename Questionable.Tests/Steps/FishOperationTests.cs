using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Common;
using Questionable.Controller.Steps.Fishing;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using static Questionable.External.AutoHookIpc;

namespace Questionable.Tests.Steps;

public sealed class FishOperationTests
{
    private readonly TestClock _clock = new();
    private readonly FishingOperationState _operation;
    private readonly AutoHookIpc _ipc;
    private readonly Fish.DoFish _executor;
    private readonly TaskQueue _queue = new(NullLogger.Instance);
    private readonly IFishingPresetGenerator _generator = Substitute.For<IFishingPresetGenerator>();
    private readonly Fish.FishTask _task = new(QuestTestData.CreateQuest(new QuestId(65000)),
        new GatheredItem { ItemId = 42, ItemCount = 3 }, []);
    private EOwnedFishingState _status = EOwnedFishingState.Starting;
    private EOwnedStartResult _startResult = EOwnedStartResult.Accepted;
    private bool _supported = true;
    private bool _idle = true;
    private bool _unavailable;
    private bool _startThrows;
    private bool _stopAccepted = true;
    private bool _objectives;
    private bool _started;
    private int _starts;
    private readonly List<Guid> _stops = [];
    private Guid _submitted;

    public FishOperationTests()
    {
        _operation = new FishingOperationState(_clock);
        _ipc = new AutoHookIpc(NullLogger<AutoHookIpc>.Instance, _operation,
            () => _unavailable ? throw new InvalidOperationException("unloaded") : _supported ? 1 : 0,
            () => _idle,
            (id, _) =>
            {
                _submitted = id;
                ++_starts;
                if (_startThrows)
                    throw new InvalidOperationException("reply lost after acceptance");
                return ((int)_startResult, "start result");
            },
            _ => ((int)_status, _started, "provider status"),
            id => { _stops.Add(id); return _stopAccepted; });
        _generator.CreatePresetFromTask(Arg.Any<Fish.FishTask>()).Returns("preset");
        _executor = new Fish.DoFish(_ipc, _operation, _generator, NullLogger<Fish.DoFish>.Instance,
            _ => _objectives, _clock);
    }

    [Fact]
    public void Preparation_ProducesActionsOnlyAfterChecksAndPresetGeneration()
    {
        Assert.True(_queue.StartTask(_executor, _task));
        Assert.Equal(ETaskResult.CreateNewTasks, UpdateExecutor());
        var tasks = _executor.CreateExtraTasks().ToArray();
        Assert.IsType<MountStep.UnmountTask>(tasks[0]);
        Assert.IsType<SwitchClassJob.Task>(tasks[1]);
        Assert.Equal("preset", Assert.IsType<Fish.FishTask>(tasks[2]).PreparedPreset);
        Assert.Equal(0, _starts);
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void UnsupportedProvider_HardFailsUnmetObjectiveWithoutMutation()
    {
        _supported = false;
        Assert.Throws<TaskException>(() => _queue.StartTask(_executor, _task));
        Assert.Equal(0, _starts);
        Assert.Empty(_stops);
        _generator.DidNotReceiveWithAnyArgs().CreatePresetFromTask(default!);
    }

    [Fact]
    public void SatisfiedObjective_SkipsUnavailableProvider()
    {
        _unavailable = true;
        _objectives = true;
        Assert.False(_queue.StartTask(_executor, _task));
        Assert.Equal(0, _starts);
        Assert.Empty(_stops);
    }

    [Fact]
    public void ManualFishing_BlocksBeforePreparationAndIsNeverStopped()
    {
        _idle = false;
        Assert.Throws<TaskException>(() => _queue.StartTask(_executor, _task));
        _ipc.CancelOwned();
        Assert.Equal(0, _starts);
        Assert.Empty(_stops);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectedStart_ReleasesSubmissionWithoutCancellation(int result)
    {
        _startResult = (EOwnedStartResult)result;
        Assert.Throws<TaskException>(Start);
        Assert.False(_operation.HasOperation);
        Assert.Empty(_stops);
    }

    [Fact]
    public void AmbiguousStart_RetainsClientIdAcrossQueueRollback()
    {
        _startThrows = true;
        Assert.Throws<TaskException>(Start);
        Assert.True(_operation.HasOperation);
        Assert.True(_operation.IsAbandoned);
        Assert.True(_operation.RequiresReconciliation);
        Assert.Equal(_submitted, _operation.OperationId);
        Assert.Equal(_submitted, Assert.Single(_stops));
        _status = EOwnedFishingState.Stopped;
        _ipc.PollOperation();
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void ObjectiveStop_RetainsActiveOwnerUntilActualStopAcknowledgement()
    {
        Start();
        _status = EOwnedFishingState.Running;
        _started = true;
        _objectives = true;
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        Assert.False(_operation.IsAbandoned);
        Assert.True(_operation.HasStarted);
        Assert.Equal(_submitted, Assert.Single(_stops));
        _status = EOwnedFishingState.Stopping;
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        Assert.True(_operation.HasOperation);
        _status = EOwnedFishingState.Stopped;
        Assert.Equal(ETaskResult.TaskComplete, UpdateExecutor());
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void StopBeforeFirstCast_CancelsPendingStartWithoutClaimingItStarted()
    {
        Start();
        _queue.Reset();
        Assert.True(_operation.HasOperation);
        Assert.False(_operation.HasStarted);
        Assert.True(_operation.IsAbandoned);
        _status = EOwnedFishingState.Stopping;
        _ipc.PollOperation();
        Assert.True(_operation.HasOperation);
        _status = EOwnedFishingState.Stopped;
        _ipc.PollOperation();
        Assert.False(_operation.HasOperation);
    }

    [Fact]
    public void RefusedNormalStop_IsRetriedWithoutAbandoningOwner()
    {
        Start();
        _stopAccepted = false;
        _objectives = true;
        UpdateExecutor();
        UpdateExecutor();
        Assert.Single(_stops);
        _clock.Advance(TimeSpan.FromSeconds(2));
        UpdateExecutor();
        Assert.Equal(2, _stops.Count);
        Assert.False(_operation.IsAbandoned);
    }

    [Fact]
    public void ProviderLoss_IsNeverIdleAndCannotReleaseGuard()
    {
        Start();
        _unavailable = true;
        Assert.Null(_ipc.GetFishingBusy());
        _ipc.PollOperation();
        _clock.Advance(TimeSpan.FromSeconds(10));
        _ipc.PollOperation();
        Assert.True(_operation.RequiresReconciliation);
        Assert.True(_operation.HasOperation);
        Assert.False(_operation.TryConfirmIdle());
    }

    [Fact]
    public void OwnershipLost_ImmediatelyRequiresReconciliationAndNeverStopsReplacement()
    {
        Start();
        _status = EOwnedFishingState.OwnershipLost;
        _idle = false;
        Assert.Throws<TaskException>(() => UpdateExecutor());
        _queue.Reset();
        Assert.True(_operation.HasOperation);
        Assert.True(_operation.RequiresReconciliation);
        Assert.Empty(_stops);
        Assert.False(_operation.TryConfirmIdle());
        _idle = true;
        _ipc.ObserveIdle();
        for (int i = 0; i < 4; ++i)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _ipc.ObserveIdle();
        }
        Assert.True(_operation.CanConfirmIdle);
        Assert.True(_operation.TryConfirmIdle());
    }

    [Fact]
    public void MissingStartAcknowledgement_TimesOutWithRetainedStopGuard()
    {
        Start();
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Throws<TaskException>(() => UpdateExecutor());
        Assert.True(_operation.RequiresReconciliation);
        Assert.True(_operation.HasOperation);
        Assert.Single(_stops);
        Assert.Equal(1, _starts);
    }

    [Fact]
    public void StoppedWithoutObjectives_FailsAfterInventoryGracePeriod()
    {
        Start();
        _status = EOwnedFishingState.Stopped;
        Assert.Equal(ETaskResult.StillRunning, UpdateExecutor());
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Throws<TaskException>(() => UpdateExecutor());
        Assert.False(_operation.HasOperation);
    }

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
        var task = _task with { GatheredItem = hasItem ? _task.GatheredItem : null };
        // Construct afresh: completion-flag presence is cached by the record constructor.
        task = new Fish.FishTask(task.Quest, task.GatheredItem,
            [null, new QuestWorkValue(1, 1, EQuestWorkMode.Bitwise), null, null, null, null]);
        Assert.True(Fish.ObjectivesSatisfied(task, true, 0));
        Assert.False(Fish.ObjectivesSatisfied(task, false, 0));
    }

    private ETaskResult UpdateExecutor()
    {
        _ipc.PollOperation();
        return _executor.Update();
    }
    private void Start() => Assert.True(_queue.StartTask(_executor, _task with { PreparedPreset = "preset" }));
}
