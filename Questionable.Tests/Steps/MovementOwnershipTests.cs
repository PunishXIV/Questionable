using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class MovementOwnershipTests : IDisposable
{
    private readonly List<AutoDutyIpc> _ownedAutoDuties = [];

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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RetainedGathering_ClearsLocalMovementWithoutTouchingProviderTravel(bool abandoned, bool uncertain)
    {
        GatheringOperationState operation = new();
        Guid owner = operation.BeginSubmission();
        // Legacy gathering has no remote operation ID; local retention alone must
        // protect travel, including when the provider reports idle or its reply was lost.
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
    [InlineData(false)]
    [InlineData(true)]
    public void ManualAutoDuty_StopDoesNotInterruptProviderTravel(bool statusFailure)
    {
        using var duty = CreateAutoDuty(new(), stopped: false, statusFailure: statusFailure);
        using MovementController movement = CreateController(new(), Substitute.For<IKnightshopperIpc>(), autoDuty: duty);

        movement.Stop();

        Assert.Null(movement.Destination);
        Assert.False(duty.HasOperation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedAutoDuty_ProtectsTravelAfterQueueResetOrLostStatus(bool uncertain)
    {
        AutoDutyOperationState operation = new();
        Guid owner = operation.BeginSubmission(123);
        operation.MarkRunSubmitted();
        operation.AbandonCurrent();
        if (uncertain)
            operation.RequireReconciliation("Provider status lost", providerLost: true);
        using var duty = CreateAutoDuty(operation, stopped: true);
        using MovementController movement = CreateController(new(), Substitute.For<IKnightshopperIpc>(), autoDuty: duty);

        AssertExternalMovementProtected(movement);

        Assert.Equal(owner, operation.OperationId);
        Assert.True(operation.IsAbandoned);
        Assert.Equal(uncertain, operation.RequiresReconciliation);
    }

    private static AutoDutyIpc CreateAutoDuty(AutoDutyOperationState operation, bool stopped, bool statusFailure = false)
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        var isStopped = Substitute.For<ICallGateSubscriber<bool>>();
        isStopped.HasFunction.Returns(true);
        isStopped.InvokeFunc().Returns(_ =>
        {
            if (statusFailure)
                throw new InvalidOperationException("Provider status lost");
            return stopped;
        });
        pluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped").Returns(isStopped);
        var run = Substitute.For<ICallGateSubscriber<uint, int, bool, object>>();
        run.HasAction.Returns(true);
        pluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run").Returns(run);
        var stop = Substitute.For<ICallGateSubscriber<object>>();
        stop.HasAction.Returns(true);
        pluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop").Returns(stop);
        return new AutoDutyIpc(pluginInterface, new(), _ => null, NullLogger<AutoDutyIpc>.Instance,
            operation, () => default);
    }

    private MovementController CreateController(PurchaseOperationState operation, IKnightshopperIpc knightshopper,
        GatheringOperationState? gatheringOperation = null, IGatherChillIpc? gatherChill = null,
        FishingOperationState? fishingOperation = null, AutoDutyIpc? autoDuty = null)
    {
        var framework = Substitute.For<IFramework>();
        framework.IsFrameworkUnloading.Returns(true);
        if (autoDuty == null)
        {
            autoDuty = CreateAutoDuty(new(), stopped: true);
            _ownedAutoDuties.Add(autoDuty);
        }

        // Native dependencies are deliberately null: reaching the global navmesh stop fails
        // these tests before any game input can be touched.
        return new MovementController(
            navmeshIpc: null!,
            clientState: null!,
            gameFunctions: null!,
            chatFunctions: null!,
            condition: null!,
            movementOverrideController: null!,
            objectTable: null!,
            aetheryteData: null!,
            chatGui: null!,
            framework: framework,
            purchaseOperation: operation,
            knightshopperIpc: knightshopper,
            gatheringOperation: gatheringOperation ?? new(),
            gatherChillIpc: gatherChill ?? Substitute.For<IGatherChillIpc>(),
            fishingOperation: fishingOperation ?? new(),
            autoDutyIpc: autoDuty,
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
        foreach (var autoDuty in _ownedAutoDuties)
            autoDuty.Dispose();
    }
}
