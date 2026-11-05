using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Shared;
using Questionable.External;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ExternalOperationCoordinatorTests
{
    [Fact]
    public void LaterProvidersSeeExecutorDetachedByEarlierReconciliation()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.BeginSubmission();
        fixture.Gathering.BeginSubmission();
        fixture.Artisan.GetStatus().Returns(ArtisanStatus.Unavailable);
        var host = new Host
        {
            IsActive = true,
            CurrentExecutor = new Gather.StartGathering(fixture.GatheringController),
        };

        Assert.True(fixture.Coordinator.Update(host));

        Assert.False(host.IsActive);
        Assert.Null(host.CurrentExecutor);
        fixture.Gather.Received(1).CancelOwned();
        fixture.Gather.Received(1).PollOperation();
    }

    [Fact]
    public void CombatCleanupStopsSchedulingBeforeSuccessfulRetryReleasesTheBarrier()
    {
        var module = Substitute.For<ICombatModule>();
        module.CanHandleFight(Arg.Any<CombatController.CombatData>()).Returns(true);
        module.Start(Arg.Any<CombatController.CombatData>()).Returns(true);
        using var fixture = new ExternalOperationFixture([module]);
        Assert.True(fixture.Combat.Start(CombatTestData.Create()));
        fixture.Combat.Stop("provider not ready to release");
        Assert.True(fixture.Combat.HasPendingCleanup);
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
        var host = new Host { IsActive = true };
        module.Stop().Returns(_ =>
        {
            Assert.False(host.IsActive);
            Assert.Equal(1, host.StopCount);
            return true;
        });

        Assert.False(fixture.Coordinator.Update(host));

        Assert.False(fixture.Combat.HasPendingCleanup);
        Assert.False(fixture.Coordinator.HasExternalOperation);
        Assert.False(host.IsActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedOwnerStopsSchedulingBeforeTerminalPollCanReleaseIt(bool alreadyAbandoned)
    {
        using var fixture = new ExternalOperationFixture();
        var operation = fixture.Purchase;
        Guid owner = operation.BeginSubmission();
        operation.AcceptSubmission(owner, Guid.NewGuid());
        if (alreadyAbandoned)
            operation.AbandonCurrent();
        var queue = new TaskQueue(NullLogger.Instance);
        queue.Enqueue(new WaitAtEnd.WaitNextStepOrSequence());
        List<string> events = [];
        var host = new Host { IsActive = true };
        host.OnStop = () =>
        {
            events.Add("stop");
            fixture.Coordinator.CancelAll(queue.Reset);
        };
        fixture.Knightshopper.When(x => x.CancelOwned()).Do(_ =>
        {
            Assert.True(queue.AllTasksComplete);
            events.Add("cancel");
            operation.AbandonCurrent();
        });
        fixture.Knightshopper.When(x => x.PollOperation()).Do(_ =>
        {
            Assert.False(host.IsActive);
            Assert.True(queue.AllTasksComplete);
            events.Add("terminal");
            operation.ObserveStatus(owner, ExternalOperationStatus.Succeeded, "finished");
        });

        Assert.False(fixture.Coordinator.Update(host));

        Assert.Equal(["stop", "cancel", "cancel", "terminal"], events);
        Assert.False(fixture.Coordinator.HasExternalOperation);
        Assert.False(host.IsActive);
        Assert.True(queue.AllTasksComplete);
        Assert.True(fixture.Coordinator.CanUpdateTask(null));
    }

    [Fact]
    public void EveryOwnerIsPolledEvenAfterEarlierProviderBlocksOrThrows()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.BeginSubmission();
        fixture.Purchase.BeginSubmission();
        fixture.Gathering.BeginSubmission();
        fixture.Fishing.BeginSubmission();
        fixture.Duty.Start();
        fixture.Duty.Available = false;
        fixture.BossMod.Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        fixture.BossMod.Available = false;
        fixture.Artisan.GetStatus().Returns(_ => throw new InvalidOperationException("Artisan unavailable"));
        fixture.Knightshopper.When(x => x.PollOperation()).Do(_ => throw new InvalidOperationException("status failed"));
        var host = new Host { IsActive = true };

        Assert.True(fixture.Coordinator.Update(host));

        fixture.Artisan.Received(1).GetStatus();
        fixture.Knightshopper.Received(1).PollOperation();
        fixture.Gather.Received(1).PollOperation();
        fixture.Feesh.Received(1).PollOperation();
        Assert.True(fixture.Duty.State.IsAbandoned);
        Assert.True(fixture.BossMod.Ipc.HasPendingCleanup);
        Assert.True(fixture.Crafting.RequiresReconciliation);
        Assert.False(host.IsActive);
        Assert.False(fixture.Coordinator.CanPrepareQuestTasks(out _));
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
    }

    [Fact]
    public void CancellationContinuesAfterQueueAndProviderFailuresInOriginalOrder()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.BeginSubmission();
        fixture.Purchase.BeginSubmission();
        fixture.Duty.Start();
        fixture.Duty.ThrowStop = true;
        fixture.BossMod.Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        fixture.BossMod.Available = false;
        List<string> order = [];
        fixture.Knightshopper.When(x => x.CancelOwned()).Do(_ =>
        {
            order.Add("purchase");
            throw new InvalidOperationException("cancel failed");
        });
        fixture.Feesh.When(x => x.CancelOwned()).Do(_ => order.Add("fishing"));
        fixture.Gather.When(x => x.CancelOwned()).Do(_ => order.Add("gathering"));

        Assert.False(fixture.Coordinator.CancelAll(() =>
        {
            Assert.True(fixture.Crafting.RequiresReconciliation);
            order.Add("queue");
            return false;
        }));

        Assert.Equal(["queue", "purchase", "fishing", "gathering"], order);
        fixture.Duty.Stop.Received(1).InvokeAction();
        Assert.True(fixture.BossMod.Ipc.HasPendingCleanup);
        Assert.True(fixture.Coordinator.HasExternalOperation);
    }

    [Theory]
    [InlineData("purchase")]
    [InlineData("gathering")]
    [InlineData("fishing")]
    public void MissingManualProviderStatusBlocksPreparation(string provider)
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Knightshopper.IsReady().Returns(provider == "purchase");
        fixture.Knightshopper.GetBusy().Returns((bool?)null);
        fixture.Gather.IsAvailable().Returns(provider == "gathering");
        fixture.Gather.GetBusy().Returns((bool?)null);
        fixture.Feesh.IsAvailable().Returns(provider == "fishing");
        fixture.Feesh.GetBusy().Returns((bool?)null);

        Assert.False(fixture.Coordinator.CanPrepareQuestTasks(out string? reason));
        Assert.Contains("status is unavailable", reason);
        Assert.False(fixture.Coordinator.HasExternalOperation);
    }

    [Fact]
    public void MissingDutyStatusBlocksPreparation()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Duty.StoppedGate.InvokeFunc().Returns(_ => throw new InvalidOperationException("status unavailable"));

        Assert.False(fixture.Coordinator.CanPrepareQuestTasks(out string? reason));
        Assert.Contains("AutoDuty", reason);
    }

    [Fact]
    public void CraftConfirmationRequiresFreshIdleAndNeverResumes()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.BeginSubmission();
        fixture.Crafting.AbandonCurrent();
        fixture.Artisan.GetStatus().Returns(ArtisanStatus.Idle);
        var host = new Host();

        Assert.False(fixture.Coordinator.TryConfirmArtisanIdle(host));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(fixture.Coordinator.TryConfirmArtisanIdle(host));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(fixture.Coordinator.TryConfirmArtisanIdle(host));

        Assert.False(fixture.Crafting.HasOperation);
        Assert.False(host.IsActive);
        Assert.Null(host.CurrentExecutor);
        Assert.Equal(0, host.StopCount);
        Assert.Null(host.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedConfirmationRefreshesStatusAndKeepsOtherOwnersBlocked(bool becameBusy)
    {
        using var fixture = new ExternalOperationFixture();
        Guid owner = fixture.Purchase.BeginSubmission();
        fixture.Purchase.MarkUncertain(owner);
        fixture.Fishing.BeginSubmission();
        fixture.Purchase.ObserveIdle(false);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Purchase.ObserveIdle(false);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        List<string> order = [];
        fixture.Knightshopper.When(x => x.PollOperation()).Do(_ =>
        {
            order.Add("status");
            if (becameBusy)
                fixture.Purchase.ObserveStatus(owner, ExternalOperationStatus.Running, "new work");
        });
        fixture.Knightshopper.When(x => x.ObserveIdle()).Do(_ =>
        {
            order.Add("idle");
            fixture.Purchase.ObserveIdle(becameBusy);
        });
        var host = new Host();

        Assert.Equal(!becameBusy, fixture.Coordinator.TryConfirmKnightshopperIdle(host));

        Assert.Equal(["status", "idle"], order);
        Assert.Equal(becameBusy, fixture.Purchase.HasOperation);
        Assert.True(fixture.Coordinator.HasExternalOperation);
        Assert.False(fixture.Coordinator.CanPrepareQuestTasks(out _));
        Assert.False(host.IsActive);
        Assert.Null(host.CurrentExecutor);
        if (!becameBusy)
            Assert.Equal(fixture.Fishing.BlockReason, host.Status);
    }

    [Fact]
    public void ActiveSchedulingRejectsEveryConfirmationWithoutReadingProviders()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.MarkUncertain(fixture.Crafting.BeginSubmission());
        fixture.Purchase.MarkUncertain(fixture.Purchase.BeginSubmission());
        fixture.Gathering.MarkUncertain(fixture.Gathering.BeginSubmission());
        fixture.Fishing.MarkUncertain(fixture.Fishing.BeginSubmission());
        var host = new Host { IsActive = true };

        Assert.False(fixture.Coordinator.TryConfirmArtisanIdle(host));
        Assert.False(fixture.Coordinator.TryConfirmKnightshopperIdle(host));
        Assert.False(fixture.Coordinator.TryConfirmGatherChillIdle(host));
        Assert.False(fixture.Coordinator.TryConfirmFeeshIdle(host));
        Assert.False(fixture.Coordinator.TryConfirmAutoDutyIdle(host));
        Assert.False(fixture.Coordinator.TryConfirmBossModStopped(host));

        fixture.Artisan.DidNotReceive().GetStatus();
        fixture.Knightshopper.DidNotReceive().PollOperation();
        fixture.Gather.DidNotReceive().PollOperation();
        fixture.Feesh.DidNotReceive().PollOperation();
        Assert.True(host.IsActive);
    }

    [Theory]
    [InlineData("crafting")]
    [InlineData("purchase")]
    [InlineData("gathering")]
    [InlineData("fishing")]
    [InlineData("duty")]
    public void OnlyTheMatchingHealthyExecutorCanContinueWhileOwnerIsRetained(string provider)
    {
        using var fixture = new ExternalOperationFixture();
        ITaskExecutor executor;
        switch (provider)
        {
            case "crafting":
                fixture.Crafting.BeginSubmission();
                executor = new Craft.DoCraft(Substitute.For<IDataManager>(), fixture.Artisan,
                    NullLogger<Craft.DoCraft>.Instance, null!, fixture.Crafting);
                break;
            case "purchase":
                fixture.Purchase.BeginSubmission();
                executor = new Purchase.DoBuy(fixture.Knightshopper, fixture.Purchase, _ => 0, () => { });
                break;
            case "gathering":
                fixture.Gathering.BeginSubmission();
                executor = new Gather.StartGathering(fixture.GatheringController);
                break;
            case "fishing":
                fixture.Fishing.BeginSubmission();
                executor = new Fish.DoFish(fixture.Feesh, fixture.Fishing, NullLogger<Fish.DoFish>.Instance,
                    _ => false, _ => 0, _ => "Quest fish");
                break;
            default:
                fixture.Duty.Start();
                executor = new Duty.StartAutoDutyExecutor(fixture.Duty.Ipc, _ => AutoDutyFixture.Content,
                    new(), () => 100, _ => { });
                break;
        }

        Assert.True(fixture.Coordinator.CanUpdateTask(executor));
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
        Assert.False(fixture.Coordinator.CanUpdateTask(new WaitAtEnd.WaitNextStepOrSequenceExecutor()));
        fixture.Crafting.AbandonCurrent();
        fixture.Purchase.AbandonCurrent();
        fixture.Gathering.AbandonCurrent();
        fixture.Fishing.AbandonCurrent();
        fixture.Duty.State.AbandonCurrent();
        Assert.False(fixture.Coordinator.CanUpdateTask(executor));
    }

    [Fact]
    public void HealthySoloDutyAllowsLocalTasksButLostOwnershipStopsScheduling()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.BossMod.Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        var host = new Host { IsActive = true };
        Assert.True(fixture.Coordinator.HasOwnedSoloDuty);
        Assert.True(fixture.Coordinator.CanUpdateTask(null));
        fixture.BossMod.Available = false;

        Assert.True(fixture.Coordinator.Update(host));

        Assert.False(host.IsActive);
        Assert.Equal(1, host.StopCount);
        Assert.True(fixture.Coordinator.HasExternalOperation);
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
    }

    private sealed class Host : IExternalOperationHost
    {
        public ITaskExecutor? CurrentExecutor { get; set; }
        public bool IsActive { get; set; }
        public string? Status { get; set; }
        public Action? OnStop { get; set; }
        public int StopCount { get; private set; }

        public void Stop(string reason)
        {
            ++StopCount;
            CurrentExecutor = null;
            OnStop?.Invoke();
            IsActive = false;
        }
    }
}
