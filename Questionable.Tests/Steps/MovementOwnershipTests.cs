using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class MovementOwnershipTests : IDisposable
{
    private readonly List<AutoDutyIpc> _ownedAutoDuties = [];
    private readonly List<AutoDutyOperationProvider> _ownedDutyProviders = [];

    [Fact]
    public void ManualPurchase_StopClearsLocalDestinationWithoutStoppingProviderTravel()
    {
        PurchaseOperationState operation = new();
        var knightshopper = Substitute.For<IKnightshopperIpc>();
        using MovementController movement = CreateController(operation, knightshopper);
        MovementController.DestinationData destination = movement.Destination!;
        knightshopper.GetBusy().Returns(_ =>
        {
            Assert.Same(destination, movement.Destination); // Snapshot provider activity before local cleanup.
            return true;
        });

        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.False(operation.HasOperation);
        knightshopper.Received(1).GetBusy();
        knightshopper.DidNotReceive().CancelOwned();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedPurchase_StopPreservesTravelEvenWhenProviderReportsIdle(bool cancellationPending)
    {
        PurchaseOperationState operation = new();
        Guid owner = operation.BeginSubmission();
        Guid providerId = Guid.NewGuid();
        operation.AcceptSubmission(owner, providerId);
        if (cancellationPending)
        {
            operation.AbandonCurrent();
            operation.ObserveStatus(owner, ExternalOperationStatus.CancellationRequested, "stopping");
        }

        var knightshopper = Substitute.For<IKnightshopperIpc>();
        knightshopper.GetBusy().Returns((bool?)false);
        using MovementController movement = CreateController(operation, knightshopper);

        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.Equal(owner, operation.OperationId);
        Assert.Equal(providerId, operation.ProviderOperationId);
        Assert.Equal(cancellationPending, operation.IsAbandoned);
        knightshopper.DidNotReceive().CancelOwned();
    }

    [Fact]
    public void OwnershipAcquiredDuringBusyQuery_StillProtectsProviderTravel()
    {
        PurchaseOperationState operation = new();
        var knightshopper = Substitute.For<IKnightshopperIpc>();
        using MovementController movement = CreateController(operation, knightshopper);
        knightshopper.GetBusy().Returns(_ =>
        {
            operation.AcceptSubmission(operation.BeginSubmission(), Guid.NewGuid());
            return false;
        });

        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.True(operation.HasOperation);
        knightshopper.DidNotReceive().CancelOwned();
    }

    [Fact]
    public void ManualGathering_StopClearsLocalDestinationWithoutStoppingProviderTravel()
    {
        GatheringOperationState operation = new();
        var knightshopper = Substitute.For<IKnightshopperIpc>();
        knightshopper.GetBusy().Returns((bool?)false);
        var gatherChill = Substitute.For<IGatherChillIpc>();
        using MovementController movement = CreateController(new(), knightshopper, operation, gatherChill);
        MovementController.DestinationData destination = movement.Destination!;
        gatherChill.GetBusy().Returns(_ =>
        {
            Assert.Same(destination, movement.Destination);
            return true;
        });

        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.False(operation.HasOperation);
        gatherChill.Received(1).GetBusy();
        gatherChill.DidNotReceive().CancelOwned();
        knightshopper.DidNotReceive().CancelOwned();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void OlderGatheringApi_BusyOrUnreadableStatusProtectsProviderTravel(int version, bool unreadable)
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        var apiVersion = Substitute.For<ICallGateSubscriber<int>>();
        var busy = Substitute.For<ICallGateSubscriber<bool>>();
        var cancel = Substitute.For<ICallGateSubscriber<string, Guid, bool>>();
        apiVersion.InvokeFunc().Returns(version);
        busy.InvokeFunc().Returns(_ => unreadable ? throw new InvalidOperationException("status unavailable") : true);
        plugin.GetIpcSubscriber<int>("GatherChill.ApiVersion").Returns(apiVersion);
        plugin.GetIpcSubscriber<bool>("GatherChill.IsBusy").Returns(busy);
        plugin.GetIpcSubscriber<string, Guid, bool>("GatherChill.CancelJob").Returns(cancel);
        GatheringOperationState operation = new();
        var gather = new GatherChillIpc(plugin, NullLogger<GatherChillIpc>.Instance, operation);
        var knightshopper = Substitute.For<IKnightshopperIpc>();
        knightshopper.GetBusy().Returns(false);
        using MovementController movement = CreateController(new(), knightshopper, operation, gather);

        // Old providers stay visible to the movement guard; the null navmesh fails if Stop touches global movement.
        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.True(gather.IsAvailable());
        Assert.False(gather.IsReady());
        Assert.False(operation.HasOperation);
        cancel.DidNotReceiveWithAnyArgs().InvokeFunc(default!, default);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RetainedGathering_ClearsLocalMovementWithoutTouchingProviderTravel(bool abandoned, bool uncertain)
    {
        GatheringOperationState operation = new();
        Guid owner = operation.BeginSubmission();
        // Legacy gathering has no remote ID, so local retention alone protects travel, even after idle or a lost reply.
        if (abandoned)
            operation.AbandonCurrent();
        if (uncertain)
            operation.MarkUncertain(owner);
        bool requiresReconciliation = operation.RequiresReconciliation;

        var knightshopper = Substitute.For<IKnightshopperIpc>();
        knightshopper.GetBusy().Returns((bool?)false);
        var gatherChill = Substitute.For<IGatherChillIpc>();
        gatherChill.GetBusy().Returns((bool?)false);
        using MovementController movement = CreateController(new(), knightshopper, operation, gatherChill);

        AssertExternalMovementProtected(movement);

        Assert.Equal(owner, operation.OperationId);
        Assert.Null(operation.ProviderOperationId);
        Assert.Equal(abandoned, operation.IsAbandoned);
        Assert.Equal(requiresReconciliation, operation.RequiresReconciliation);
        gatherChill.DidNotReceive().CancelOwned();
        knightshopper.DidNotReceive().CancelOwned();
    }

    [Fact]
    public void GatheringOwnershipAcquiredDuringBusyQuery_StillProtectsProviderTravel()
    {
        GatheringOperationState operation = new();
        var knightshopper = Substitute.For<IKnightshopperIpc>();
        knightshopper.GetBusy().Returns((bool?)false);
        var gatherChill = Substitute.For<IGatherChillIpc>();
        using MovementController movement = CreateController(new(), knightshopper, operation, gatherChill);
        gatherChill.GetBusy().Returns(_ =>
        {
            operation.BeginSubmission();
            return false;
        });

        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.True(operation.HasOperation);
        gatherChill.DidNotReceive().CancelOwned();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RetainedFishing_ClearsLocalMovementWithoutTouchingProviderTravel(bool abandoned, bool uncertain)
    {
        FishingOperationState operation = new();
        Guid owner = operation.BeginSubmission();
        Guid providerId = Guid.NewGuid();
        operation.AcceptSubmission(owner, providerId);
        if (abandoned)
            operation.AbandonCurrent();
        if (uncertain)
            operation.MarkUncertain(owner);

        var knightshopper = Substitute.For<IKnightshopperIpc>();
        knightshopper.GetBusy().Returns((bool?)false);
        var gatherChill = Substitute.For<IGatherChillIpc>();
        gatherChill.GetBusy().Returns((bool?)false);
        using MovementController movement = CreateController(new(), knightshopper,
            gatherChill: gatherChill, fishingOperation: operation);

        AssertExternalMovementProtected(movement);

        Assert.Equal(owner, operation.OperationId);
        Assert.Equal(providerId, operation.ProviderOperationId);
        Assert.Equal(abandoned, operation.IsAbandoned);
        Assert.Equal(uncertain, operation.RequiresReconciliation);
        gatherChill.DidNotReceive().CancelOwned();
        knightshopper.DidNotReceive().CancelOwned();
    }

    private static void AssertExternalMovementProtected(MovementController movement)
    {
        MovementController.DestinationData destination = movement.Destination!;
        movement.Stop();
        Assert.Null(movement.Destination);

        movement.Destination = destination;
        movement.Update();
        Assert.Null(movement.Destination);

        movement.Destination = destination;
        movement.NavigateTo(default, null, Vector3.One, new MovementController.NavigationOptions());
        Assert.Null(movement.Destination);

        movement.Destination = destination;
        List<Vector3> route = [Vector3.One];
        movement.NavigateTo(default, null, route, new MovementController.NavigationOptions());
        Assert.Null(movement.Destination);
        Assert.Equal(Vector3.One, Assert.Single(route));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ManualFeesh_StopPreservesTravelWithoutQuestionableOwner(bool unknown, bool localDestination)
    {
        var plugin = Substitute.For<IDalamudPluginInterface>();
        var stop = Substitute.For<ICallGateSubscriber<object>>();
        plugin.GetIpcSubscriber<object>("vnavmesh.Path.Stop").Returns(stop);
        stop.When(x => x.InvokeAction()).Do(_ => throw new InvalidOperationException("Manual route was stopped"));
        var navmesh = new NavmeshIpc(plugin, NullLogger<NavmeshIpc>.Instance);
        var feesh = Substitute.For<IFeeshIpc>();
        feesh.IsAvailable().Returns(true);
        feesh.GetBusy().Returns(unknown ? (bool?)null : true);
        var fishing = new FishingOperationState();
        using MovementController movement = CreateController(new(), Substitute.For<IKnightshopperIpc>(),
            fishingOperation: fishing, feesh: feesh, navmesh: navmesh);
        if (!localDestination)
            movement.Destination = null;

        // UI Stop and Skip both reach this path; no pending SimpleMove or QST-owned catch protects it.
        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.False(fishing.HasOperation);
        feesh.Received(1).GetBusy();
        feesh.DidNotReceive().CancelOwned();
        stop.DidNotReceive().InvokeAction();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualAutoDuty_StopDoesNotInterruptProviderTravel(bool statusFailure)
    {
        using var duty = CreateAutoDuty(stopped: false, statusFailure: statusFailure);
        using MovementController movement = CreateController(new(), Substitute.For<IKnightshopperIpc>(), autoDuty: duty);

        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.False(_ownedDutyProviders.Single().Operation.HasOperation);
    }

    private static AutoDutyIpc CreateAutoDuty(bool stopped, bool statusFailure = false) =>
        new(() => statusFailure ? throw new InvalidOperationException("Provider status lost") : stopped);

    private MovementController CreateController(PurchaseOperationState operation, IKnightshopperIpc knightshopper,
        GatheringOperationState? gatheringOperation = null, IGatherChillIpc? gatherChill = null,
        FishingOperationState? fishingOperation = null, AutoDutyIpc? autoDuty = null, IFeeshIpc? feesh = null,
        NavmeshIpc? navmesh = null)
    {
        var framework = Substitute.For<IFramework>();
        framework.IsFrameworkUnloading.Returns(true);
        if (autoDuty == null)
        {
            autoDuty = CreateAutoDuty(stopped: true);
            _ownedAutoDuties.Add(autoDuty);
        }

        MovementController? controller = null;
        var dutyProvider = TestOperationProviders.AutoDuty(autoDuty);
        _ownedDutyProviders.Add(dutyProvider);
        var operations = TestOperationProviders.Coordinator(TestOperationProviders.Moving(operation, knightshopper,
            gatheringOperation ?? new(), gatherChill ?? Substitute.For<IGatherChillIpc>(), fishingOperation ?? new(),
            feesh ?? Substitute.For<IFeeshIpc>(), dutyProvider), () => controller!);

        // Native dependencies are null, so reaching the global navmesh stop fails the test before any game input.
        return controller = new MovementController(
            navmeshIpc: navmesh!,
            clientState: null!,
            gameFunctions: null!,
            chatFunctions: null!,
            condition: null!,
            movementOverrideController: null!,
            objectTable: null!,
            aetheryteData: null!,
            chatGui: null!,
            framework: framework,
            externalOperations: operations,
            questController: null!,
            logger: NullLogger<MovementController>.Instance)
        {
            Destination = new(
                MovementType: default,
                DataId: null,
                Position: Vector3.One,
                StopDistance: 1f,
                IsFlying: false,
                CanSprint: false,
                VerticalStopDistance: 1f,
                Land: false,
                UseNavmesh: true)
        };
    }

    public void Dispose()
    {
        foreach (var provider in _ownedDutyProviders)
            provider.Dispose();
        foreach (var autoDuty in _ownedAutoDuties)
            autoDuty.Dispose();
    }
}
