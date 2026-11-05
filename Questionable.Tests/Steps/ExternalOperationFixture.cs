using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.CombatModules;
using Questionable.External;

namespace Questionable.Tests.Steps;

/// <summary>Real lifecycle coordination and state, with provider IPC and game boundaries substituted.</summary>
internal sealed class ExternalOperationFixture : IDisposable
{
    public TestClock Clock { get; } = new();
    public AutoDutyFixture Duty { get; } = new();
    public BossModIpcTests.BossModTestContext BossMod { get; } = new();
    public IFramework Framework { get; } = Substitute.For<IFramework>();
    public IObjectTable Objects { get; } = Substitute.For<IObjectTable>();
    public ICondition Condition { get; } = Substitute.For<ICondition>();
    public IClientState Client { get; } = Substitute.For<IClientState>();
    public IArtisanIpc Artisan { get; } = Substitute.For<IArtisanIpc>();
    public IKnightshopperIpc Knightshopper { get; } = Substitute.For<IKnightshopperIpc>();
    public IGatherChillIpc Gather { get; } = Substitute.For<IGatherChillIpc>();
    public IFeeshIpc Feesh { get; } = Substitute.For<IFeeshIpc>();
    public CraftingOperationState Crafting { get; }
    public PurchaseOperationState Purchase { get; }
    public GatheringOperationState Gathering { get; }
    public FishingOperationState Fishing { get; }
    public CombatController Combat { get; }
    public GatheringController GatheringController { get; }
    public MovementController Movement { get; }
    public ExternalOperationCoordinator Coordinator { get; }

    public ExternalOperationFixture(IEnumerable<ICombatModule>? combatModules = null)
    {
        Framework.IsFrameworkUnloading.Returns(true);
        Crafting = new(Clock);
        Purchase = new(Clock);
        Gathering = new(Clock);
        Fishing = new(Clock);
        var navmesh = new NavmeshIpc(Substitute.For<IDalamudPluginInterface>(), NullLogger<NavmeshIpc>.Instance);
        Movement = new(navmesh, Client, null!, null!, Condition, null!, Objects,
            null!, Substitute.For<IChatGui>(), Framework, Purchase, Knightshopper, Gathering, Gather,
            Fishing, Feesh, Duty.Ipc, null!, NullLogger<MovementController>.Instance);
        Combat = new(combatModules ?? [], Movement, Substitute.For<ITargetManager>(), Objects,
            Condition, Client, null!, null!, Framework, NullLogger<CombatController>.Instance);
        GatheringController = new(Gather, Gathering, _ => 0, () => { });
        Coordinator = new(Duty.Ipc, BossMod.Ipc, Artisan, Crafting, Knightshopper, Purchase, Gather,
            Gathering, Feesh, Fishing, Combat, GatheringController, Movement,
            NullLogger<ExternalOperationCoordinator>.Instance);
    }

    public void Dispose()
    {
        Movement.Dispose();
        Combat.Dispose();
        Duty.Dispose();
    }
}
