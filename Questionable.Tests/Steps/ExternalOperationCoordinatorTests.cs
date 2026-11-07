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
        fixture.Crafting.BeginRequest();
        fixture.Gathering.BeginRequest();
        var host = new Host
        {
            IsActive = true,
            CurrentExecutor = new Gather.StartGathering(fixture.GatheringController),
        };

        Assert.True(fixture.Coordinator.Update(host));

        Assert.False(host.IsActive);
        Assert.Null(host.CurrentExecutor);
        Assert.True(fixture.Gathering.IsAbandoned);
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

    [Fact]
    public void ActiveSchedulingRejectsEveryConfirmationWithoutReadingProviders()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.MarkUncertain(fixture.Crafting.BeginRequest());
        fixture.Purchase.MarkUncertain(fixture.Purchase.BeginRequest());
        fixture.Gathering.MarkUncertain(fixture.Gathering.BeginRequest());
        fixture.Fishing.MarkUncertain(fixture.Fishing.BeginRequest());
        var host = new Host { IsActive = true };

        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<ArtisanProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<KnightshopperProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<GatherChillProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<FeeshProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<AutoDutyProvider>(), host));
        Assert.False(fixture.Coordinator.TryConfirmIdle(fixture.Provider<CombatCleanupProvider>(), host));

        Assert.True(host.IsActive);
    }

    [Theory]
    [InlineData("crafting")]
    [InlineData("purchase")]
    [InlineData("gathering")]
    [InlineData("fishing")]
    public void MatchingExecutor_ContinuesWhileRunning(string provider)
    {
        using var fixture = new ExternalOperationFixture();
        ITaskExecutor executor;
        switch (provider)
        {
            case "crafting":
                fixture.Crafting.BeginRequest();
                executor = new Craft.DoCraft(Substitute.For<IDataManager>(), fixture.Artisan,
                    NullLogger<Craft.DoCraft>.Instance, null!, fixture.Crafting);
                break;
            case "purchase":
                fixture.Purchase.BeginRequest();
                executor = new Purchase.DoBuy(fixture.Knightshopper, fixture.Purchase, fixture.Movement);
                break;
            case "gathering":
                fixture.Gathering.BeginRequest();
                executor = new Gather.StartGathering(fixture.GatheringController);
                break;
            default:
                fixture.Fishing.BeginRequest();
                executor = new Fish.DoFish(fixture.Feesh, fixture.Fishing, Substitute.For<IDataManager>(),
                    NullLogger<Fish.DoFish>.Instance, null!);
                break;
        }

        Assert.True(fixture.Coordinator.CanUpdateTask(executor));
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
        Assert.False(fixture.Coordinator.CanUpdateTask(new WaitAtEnd.WaitNextStepOrSequenceExecutor()));
        fixture.Crafting.Abandon();
        fixture.Purchase.Abandon();
        fixture.Gathering.Abandon();
        fixture.Fishing.Abandon();
        Assert.False(fixture.Coordinator.CanUpdateTask(executor));
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
        fixture.Fishing.BeginRequest();
        fixture.Purchase.BeginRequest();

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
        // AutoDuty is also used by its concrete type, so its interface registration forwards to that singleton.
        using var fixture = new ExternalOperationFixture();
        var resolver = Substitute.For<IServiceProvider>();
        resolver.GetService(typeof(AutoDutyProvider)).Returns(fixture.Provider<AutoDutyProvider>());

        Assert.Equal(
            new[]
            {
                typeof(ArtisanProvider), typeof(AutoDutyProvider), typeof(CombatCleanupProvider),
                typeof(FeeshProvider), typeof(GatherChillProvider), typeof(KnightshopperProvider),
            },
            services.Where(s => s.ServiceType == typeof(IOperationProvider))
                .Select(s => s.ImplementationType ?? s.ImplementationFactory!(resolver).GetType()).OrderBy(t => t.Name));
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
        fixture.Purchase.BeginRequest();
        fixture.Crafting.BeginRequest();
        fixture.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Crafting].Returns(crafting);
        var buy = new Purchase.DoBuy(fixture.Knightshopper, fixture.Purchase, fixture.Movement);
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
    public void MovementSeesMovingWorkButNotCrafting()
    {
        using var fixture = new ExternalOperationFixture();
        fixture.Crafting.BeginRequest();
        Assert.False(fixture.Coordinator.HasMovingOperation);

        fixture.Fishing.BeginRequest();

        Assert.True(fixture.Coordinator.HasMovingOperation);
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
        var provider = fixture.Provider<KnightshopperProvider>();
        Guid owner = fixture.Purchase.BeginRequest();

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
        fixture.Purchase.BeginRequest();
        fixture.Purchase.Abandon();
        Assert.Equal(new OperationPill("Stopping purchase", Warning: true, NeedsCheck: false),
            fixture.Coordinator.GetTitleBarPill());

        fixture.Fishing.MarkUncertain(fixture.Fishing.BeginRequest());

        Assert.Equal("Check Feesh", fixture.Coordinator.GetTitleBarPill()?.Text);
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
