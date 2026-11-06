using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Microsoft.Extensions.DependencyInjection;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Operations;
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
    public void LaterProviders_SeeStoppedExecutor()
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
    public void CombatCleanup_StopsTasksUntilFinished()
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
    public void StoppedOperation_StopsTasksBeforeStatusCheck(bool alreadyAbandoned)
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
    public void AllProviders_ArePolledAfterError()
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
    public void CancellationContinuesAfterQueueAndProviderFailuresInProviderOrder()
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

        Assert.Equal(["queue", "purchase", "gathering", "fishing"], order);
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

        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<ArtisanOperationProvider>(), host));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<ArtisanOperationProvider>(), host));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(fixture.Coordinator.TryConfirmIdle(fixture.Provider<ArtisanOperationProvider>(), host));

        Assert.False(fixture.Crafting.HasOperation);
        Assert.False(host.IsActive);
        Assert.Null(host.CurrentExecutor);
        Assert.Equal(0, host.StopCount);
        Assert.Null(host.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Confirmation_RefreshesStatusAndBlocksOtherWork(bool becameBusy)
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

        Assert.Equal(!becameBusy, fixture.Coordinator.TryConfirmIdle(fixture.Provider<KnightshopperOperationProvider>(), host));

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

        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<ArtisanOperationProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<KnightshopperOperationProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<GatherChillOperationProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<FeeshOperationProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<AutoDutyOperationProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<CombatCleanupProvider>(), host));

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
    public void MatchingExecutor_ContinuesWhileRunning(string provider)
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
    public void SoloDutyLoss_StopsTaskScheduling()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.BossMod.Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        var host = new Host { IsActive = true };
        Assert.True(fixture.Coordinator.HandlesPlayerDeath(null));
        Assert.True(fixture.Coordinator.CanUpdateTask(null));
        fixture.BossMod.Available = false;

        Assert.True(fixture.Coordinator.Update(host));

        Assert.False(host.IsActive);
        Assert.Equal(1, host.StopCount);
        Assert.True(fixture.Coordinator.HasExternalOperation);
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
    }

    [Fact]
    public void ProvidersRunInDeclaredOrderWhateverTheRegistrationOrder()
    {
        using var fixture = new ExternalOperationFixture();

        Assert.Equal(["Artisan", "Knightshopper", "Gather n Chill", "Feesh", "AutoDuty", "Combat cleanup"],
            fixture.Coordinator.Providers.Select(x => x.Name));
    }

    [Fact]
    public void EarlierProviderBlockReasonWins()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Fishing.BeginSubmission();
        fixture.Purchase.BeginSubmission();

        Assert.Equal(fixture.Purchase.BlockReason, fixture.Coordinator.BlockReason);
    }

    [Fact]
    public void ProvidersSharingAnOrderAreRejected()
    {
        using var fixture = new ExternalOperationFixture();
        var clash = Substitute.For<IOperationProvider>();
        clash.Order.Returns(fixture.Providers[0].Order);

        Assert.Throws<InvalidOperationException>(() => new ExternalOperationCoordinator(
            [.. fixture.Providers, clash], fixture.Movement,
            NullLogger<ExternalOperationCoordinator>.Instance));
    }

    [Fact]
    public void EveryProviderIsRegistered()
    {
        IServiceCollection services = new ServiceCollection().AddQuestionable();

        Assert.Equal(
            new[]
            {
                typeof(ArtisanOperationProvider), typeof(AutoDutyOperationProvider), typeof(CombatCleanupProvider),
                typeof(FeeshOperationProvider), typeof(GatherChillOperationProvider), typeof(KnightshopperOperationProvider),
            },
            services.Where(s => s.ServiceType == typeof(IOperationProvider))
                .Select(s => s.ImplementationType!).OrderBy(t => t.Name));
    }

    [Fact]
    public void AddedProviderTakesPartWithoutCoordinatorChanges()
    {
        var added = Substitute.For<IOperationProvider>();
        added.Order.Returns(700);
        added.Name.Returns("Added");
        added.HasOperation.Returns(true);
        added.MovesCharacter.Returns(true);
        added.BlockReason.Returns("Added provider is busy.");
        added.MayBeMovingCharacter().Returns(true);
        added.Update(Arg.Any<IExternalOperationHost>(), Arg.Any<IOperationContext>()).Returns(true);
        added.Cancel(Arg.Any<IOperationContext>()).Returns(true);
        added.TryConfirmIdle(Arg.Any<IExternalOperationHost>(), Arg.Any<IOperationContext>()).Returns(true);
        using var fixture = new ExternalOperationFixture(extraProviders: [added]);
        var host = new Host();

        Assert.Equal("Added", fixture.Coordinator.Providers[^1].Name);
        Assert.True(fixture.Coordinator.HasExternalOperation);
        Assert.True(fixture.Coordinator.HasMovingOperation);
        Assert.True(fixture.Coordinator.MayBeMovingCharacter());
        Assert.Equal("Added provider is busy.", fixture.Coordinator.BlockReason);
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
        Assert.False(fixture.Coordinator.CanPrepareQuestTasks(out _));
        Assert.True(fixture.Coordinator.Update(host));
        added.Received(1).Update(host, Arg.Any<IOperationContext>());
        Assert.True(fixture.Coordinator.TryConfirmIdle(added, host));

        List<string> order = [];
        added.When(x => x.Abandon()).Do(_ => order.Add("abandon"));
        added.When(x => x.Cancel(Arg.Any<IOperationContext>())).Do(_ => order.Add("cancel"));
        Assert.False(fixture.Coordinator.CancelAll(() =>
        {
            order.Add("queue");
            return true;
        }));
        Assert.Equal(["abandon", "queue", "cancel"], order);

        added.HasOperation.Returns(false);
        added.CanUpdateTask(Arg.Any<ITaskExecutor?>()).Returns(true);
        added.GetStartBlockReason().Returns("Added provider has its own work.");
        Assert.True(fixture.Coordinator.CanUpdateTask(null));
        Assert.False(fixture.Coordinator.CanPrepareQuestTasks(out string? reason));
        Assert.Equal("Added provider has its own work.", reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Executor_SelectsItsProvider(bool crafting)
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Purchase.BeginSubmission();
        fixture.Crafting.BeginSubmission();
        fixture.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Crafting].Returns(crafting);
        var buy = new Purchase.DoBuy(fixture.Knightshopper, fixture.Purchase, _ => 0, () => { });
        var craft = new Craft.DoCraft(Substitute.For<IDataManager>(), fixture.Artisan,
            NullLogger<Craft.DoCraft>.Instance, null!, fixture.Crafting);

        IOperationProvider? purchaseOwner = fixture.Coordinator.GetOwner(buy);
        IOperationProvider? craftOwner = fixture.Coordinator.GetOwner(craft);

        Assert.Equal("Knightshopper", purchaseOwner?.Name);
        Assert.True(purchaseOwner!.CanUpdateWhileOccupied);
        Assert.Equal("Artisan", craftOwner?.Name);
        Assert.Equal(crafting, craftOwner!.CanUpdateWhileOccupied);
        Assert.Null(fixture.Coordinator.GetOwner(null));
        Assert.Null(fixture.Coordinator.GetOwner(new WaitAtEnd.WaitNextStepOrSequenceExecutor()));
    }

    [Fact]
    public void OnlyAnOwnedAutoDutyRunOrSoloDutyHandlesDeath()
    {
        using var fixture = new ExternalOperationFixture();
        var duty = new Duty.StartAutoDutyExecutor(fixture.Duty.Ipc, _ => AutoDutyFixture.Content, new(), () => 100, _ => { });
        Assert.False(fixture.Coordinator.HandlesPlayerDeath(duty));

        fixture.Duty.Start();

        Assert.True(fixture.Coordinator.HandlesPlayerDeath(duty));
        Assert.False(fixture.Coordinator.HandlesPlayerDeath(null));
    }

    [Fact]
    public void MovementSeesMovingWorkAndBusyPluginsButNotCrafting()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.BeginSubmission();
        Assert.False(fixture.Coordinator.HasMovingOperation);
        Assert.False(fixture.Coordinator.MayBeMovingCharacter());

        fixture.Fishing.BeginSubmission();
        fixture.Gather.IsAvailable().Returns(true);
        fixture.Gather.GetBusy().Returns((bool?)null);

        Assert.True(fixture.Coordinator.HasMovingOperation);
        Assert.True(fixture.Coordinator.MayBeMovingCharacter());
    }

    [Fact]
    public void NoOperation_HidesStatus()
    {
        using var fixture = new ExternalOperationFixture();

        Assert.All(fixture.Providers, x => Assert.Null(x.GetStatusView()));
        Assert.Null(fixture.Coordinator.GetTitleBarPill());
    }

    [Fact]
    public void PanelShowsWorkThenAsksForConfirmationOnceUncertain()
    {
        using var fixture = new ExternalOperationFixture();
        var provider = fixture.Provider<KnightshopperOperationProvider>();
        Guid owner = fixture.Purchase.BeginSubmission();

        Assert.Equal(new OperationStatusView("Buying quest items with Knightshopper"), provider.GetStatusView());
        Assert.Equal(new OperationPill("Purchasing", Warning: false, NeedsCheck: false), fixture.Coordinator.GetTitleBarPill());

        fixture.Purchase.MarkUncertain(owner);

        OperationStatusView view = provider.GetStatusView()!;
        Assert.Equal(fixture.Purchase.BlockReason, view.Status);
        Assert.Equal("I stopped Knightshopper", view.Recovery?.ConfirmLabel);
        Assert.False(view.Recovery!.CanConfirm);
        Assert.Equal(2, view.Recovery.Instructions.Count);
        Assert.Null(view.Recovery.Detail);
        Assert.Equal(new OperationPill("Check Knightshopper", Warning: true, NeedsCheck: true),
            fixture.Coordinator.GetTitleBarPill());
    }

    [Fact]
    public void TitleBarPrefersAnyProviderNeedingACheckOverEarlierWork()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Purchase.BeginSubmission();
        fixture.Purchase.AbandonCurrent();
        Assert.Equal(new OperationPill("Stopping purchase", Warning: true, NeedsCheck: false),
            fixture.Coordinator.GetTitleBarPill());

        fixture.Fishing.MarkUncertain(fixture.Fishing.BeginSubmission());

        Assert.Equal("Check Feesh", fixture.Coordinator.GetTitleBarPill()?.Text);
    }

    [Fact]
    public void AutoDutyRecoveryShowsItsMessageAboveTheButton()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Duty.Start();
        Assert.Equal(fixture.Duty.State.BlockReason, fixture.Provider<AutoDutyOperationProvider>().GetStatusView()?.Status);

        fixture.Duty.State.RequireReconciliation("AutoDuty status was lost");

        OperationRecoveryView? recovery = fixture.Provider<AutoDutyOperationProvider>().GetStatusView()?.Recovery;
        Assert.Equal("I stopped AutoDuty", recovery?.ConfirmLabel);
        Assert.Equal("AutoDuty status was lost", recovery?.Detail);
        Assert.Single(recovery!.Instructions);
        Assert.Null(fixture.Coordinator.GetTitleBarPill());
    }

    [Fact]
    public void LostSoloDutyAsksTheUserToStopBossMod()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.BossMod.Ipc.SetPresetForSoloDuty(BossModIpc.EPreset.QuestBattle);
        Assert.Null(fixture.Provider<CombatCleanupProvider>().GetStatusView());
        fixture.BossMod.Available = false;

        fixture.Coordinator.Update(new Host { IsActive = true });

        OperationStatusView? view = fixture.Provider<CombatCleanupProvider>().GetStatusView();
        Assert.Equal(fixture.BossMod.Ipc.CleanupReason, view?.Status);
        Assert.Equal("I stopped BossMod", view?.Recovery?.ConfirmLabel);
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
