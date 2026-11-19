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

public sealed class OperationCoordinatorTests
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
            CurrentExecutor = new Gather.Executor(fixture.GatheringController),
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
        Assert.False(fixture.Coordinator.HasOperation);
        Assert.False(host.IsActive);
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
                executor = new Craft.Executor(Substitute.For<IDataManager>(), fixture.Artisan,
                    NullLogger<Craft.Executor>.Instance, null!, fixture.Crafting);
                break;
            case "purchase":
                fixture.Purchase.BeginRequest();
                executor = new Purchase.BuyExecutor(fixture.Knightshopper, fixture.Purchase, fixture.Movement);
                break;
            case "gathering":
                fixture.Gathering.BeginRequest();
                executor = new Gather.Executor(fixture.GatheringController);
                break;
            default:
                fixture.Fishing.BeginRequest();
                executor = new Fish.Executor(fixture.Feesh, fixture.Fishing, Substitute.For<IDataManager>(),
                    NullLogger<Fish.Executor>.Instance, null!);
                break;
        }

        Assert.True(fixture.Coordinator.CanUpdateTask(executor));
        Assert.False(fixture.Coordinator.CanUpdateTask(null));
        Assert.False(fixture.Coordinator.CanUpdateTask(new WaitAtEnd.NextStepOrSequenceExecutor()));
        fixture.Crafting.Abandon();
        fixture.Purchase.Abandon();
        fixture.Gathering.Abandon();
        fixture.Fishing.Abandon();
        Assert.False(fixture.Coordinator.CanUpdateTask(executor));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Purchase_OptionalFailureSkips(bool optional)
    {
        using var fixture = new ExternalOperationFixture();
        // Another purchase in progress makes the start fail before the inventory is read.
        fixture.Purchase.BeginRequest();
        IChatGui chat = Substitute.For<IChatGui>();
        var buy = new Purchase.BuyExecutor(fixture.Knightshopper, fixture.Purchase, fixture.Movement, chat);
        Purchase.BuyTask task = new(4868, 99, Optional: optional);

        if (optional)
        {
            Assert.False(buy.Start(task));
            Assert.Single(chat.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IChatGui.Print));
        }
        else
            Assert.ThrowsAny<TaskException>(() => buy.Start(task));
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
