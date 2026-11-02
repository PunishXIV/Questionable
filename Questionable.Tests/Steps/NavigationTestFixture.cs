using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.NavigationOverrides;
using Questionable.External;

namespace Questionable.Tests.Steps;

internal abstract class NavigationTestFixture : IDisposable
{
    // Reaching this boundary avoids unrelated quest updates that require the game.
    protected readonly Exception QuestBoundary = new("Quest update boundary");
    public IDalamudPluginInterface Plugin { get; } = Substitute.For<IDalamudPluginInterface>();
    public IClientState Client { get; } = Substitute.For<IClientState>();
    public IObjectTable Objects { get; } = Substitute.For<IObjectTable>();
    protected ICondition Condition { get; } = Substitute.For<ICondition>();
    public AutoDutyFixture Duty { get; } = new();
    public PurchaseOperationState Purchase { get; } = new();
    public IKnightshopperIpc Knightshopper { get; } = Substitute.For<IKnightshopperIpc>();
    public IGatherChillIpc Gather { get; } = Substitute.For<IGatherChillIpc>();
    public ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> Pathfind { get; } =
        Substitute.For<ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>>();
    public NavmeshIpc Navmesh { get; private set; } = null!;
    public MovementController Movement { get; private set; } = null!;

    protected NavigationTestFixture()
    {
        Plugin.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable")
            .Returns(Pathfind);
    }

    // Call after the individual fixture configures its IPCs and game state.
    protected void InitializeMovement(MovementController.DestinationData destination, Func<long> clock,
        MovementOverrideController? movementOverride = null)
    {
        var framework = Substitute.For<IFramework>();
        framework.IsFrameworkUnloading.Returns(true);
        Navmesh = new(Plugin, NullLogger<NavmeshIpc>.Instance);
        Movement = new(Navmesh, Client, null!, null!, Condition, movementOverride!, Objects, null!,
            Substitute.For<IChatGui>(), framework, Purchase, Knightshopper, new(), Gather, new(), Duty.Ipc,
            new Lazy<QuestController>(() => throw QuestBoundary), NullLogger<MovementController>.Instance)
        {
            Destination = destination,
            PathfindingClock = clock,
        };
    }

    public void Dispose()
    {
        Movement.ResetPathfinding();
        Movement.Dispose();
        Duty.Dispose();
    }
}
