using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Operations;
using Questionable.Data;
using Questionable.External;

namespace Questionable.Tests.Steps;

internal sealed class ExternalOperationFixture : IDisposable
{
    public TestClock Clock { get; } = new();
    public Func<bool> DutyStatus { get; set; } = () => true;
    public AutoDutyIpc AutoDuty { get; }
    private readonly AutoDutyOperationProvider _dutyProvider;
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

    public ExternalOperationFixture(IEnumerable<ICombatModule>? combatModules = null,
        IEnumerable<IOperationProvider>? extraProviders = null)
    {
        Framework.IsFrameworkUnloading.Returns(true);
        Crafting = new(Clock);
        Purchase = new(Clock);
        Gathering = new(Clock);
        Fishing = new(Clock);
        var navmesh = new NavmeshIpc(Substitute.For<IDalamudPluginInterface>(), NullLogger<NavmeshIpc>.Instance);
        Movement = new(navmesh, Client, null!, null!, Condition, null!, Objects,
            null!, Substitute.For<IChatGui>(), Framework, new Lazy<ExternalOperationCoordinator>(() => Coordinator!),
            null!, NullLogger<MovementController>.Instance);
        Combat = new(combatModules ?? [], Movement, Substitute.For<ITargetManager>(), Objects,
            Condition, Client, null!, null!, Framework, NullLogger<CombatController>.Instance);
        GatheringController = new(Gather, Gathering, _ => 0, () => { });
        AutoDuty = new(() => DutyStatus());
        _dutyProvider = TestOperationProviders.AutoDuty(AutoDuty);
        // Deliberately not in run order: the coordinator sorts by Order.
        Providers =
        [
            new CombatCleanupProvider(Combat, BossMod.Ipc, NullLogger<CombatCleanupProvider>.Instance),
            new FeeshOperationProvider(Feesh, Fishing),
            new ArtisanOperationProvider(Artisan, Crafting, Condition, NullLogger<ArtisanOperationProvider>.Instance),
            _dutyProvider,
            new GatherChillOperationProvider(Gather, Gathering),
            new KnightshopperOperationProvider(Knightshopper, Purchase, GilShop),
            .. extraProviders ?? [],
        ];
        Coordinator = new(Providers, Movement, NullLogger<ExternalOperationCoordinator>.Instance);
    }

    public IReadOnlyList<IOperationProvider> Providers { get; }
    public GilShopData GilShop { get; } = new(Substitute.For<IDataManager>());

    public T Provider<T>() where T : IOperationProvider => Providers.OfType<T>().Single();

    public void Dispose()
    {
        Movement.Dispose();
        Combat.Dispose();
        _dutyProvider.Dispose();
        AutoDuty.Dispose();
    }
}
