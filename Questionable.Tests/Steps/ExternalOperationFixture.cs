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
    public IFramework Framework { get; } = Substitute.For<IFramework>();
    public IObjectTable Objects { get; } = Substitute.For<IObjectTable>();
    public ICondition Condition { get; } = Substitute.For<ICondition>();
    public IClientState Client { get; } = Substitute.For<IClientState>();
    public ArtisanIpc Artisan { get; }
    public KnightshopperIpc Knightshopper { get; }
    public GatherChillIpc Gather { get; }
    public FeeshIpc Feesh { get; }
    public AutoDutyIpc AutoDuty { get; }
    public BossModIpc BossMod { get; }
    public NavmeshIpc Navmesh { get; }
    public CraftingState Crafting { get; }
    public PurchaseState Purchase { get; }
    public GatheringState Gathering { get; }
    public FishingState Fishing { get; }
    public CombatController Combat { get; }
    public GatheringController GatheringController { get; }
    public MovementController Movement { get; }
    public ExternalOperationCoordinator Coordinator { get; }
    private readonly AutoDutyProvider _dutyProvider;

    public ExternalOperationFixture(IEnumerable<ICombatModule>? combatModules = null,
        IEnumerable<IOperationProvider>? extraProviders = null)
    {
        NoPluginsInstalled.Ensure();
        Framework.IsFrameworkUnloading.Returns(true);
        Crafting = new(Clock);
        Purchase = new(Clock);
        Gathering = new(Clock);
        Fishing = new(Clock);
        Artisan = new(NullLogger<ArtisanIpc>.Instance, Crafting);
        Knightshopper = new(NullLogger<KnightshopperIpc>.Instance, Purchase);
        Gather = new(NullLogger<GatherChillIpc>.Instance, Gathering);
        Feesh = new(NullLogger<FeeshIpc>.Instance, Fishing, Framework);
        AutoDuty = new();
        BossMod = new(new Configuration(), null!, Substitute.For<ICommandManager>());
        Navmesh = new(NullLogger<NavmeshIpc>.Instance);
        Movement = new(Navmesh, Client, null!, null!, Condition, null!, Objects,
            null!, Substitute.For<IChatGui>(), Framework, new Lazy<ExternalOperationCoordinator>(() => Coordinator!),
            null!, NullLogger<MovementController>.Instance);
        Combat = new(combatModules ?? [], Movement, Substitute.For<ITargetManager>(), Objects,
            Condition, Client, null!, null!, Framework, NullLogger<CombatController>.Instance);
        GatheringController = new(Gather, Gathering, Movement);
        _dutyProvider = new(AutoDuty, new Configuration(), null!, NullLogger<AutoDutyProvider>.Instance,
            new(), Client, Condition, Substitute.For<IDutyState>(), Navmesh);
        // Deliberately not in run order: the coordinator sorts by Order.
        Providers =
        [
            new CombatCleanupProvider(Combat, BossMod, NullLogger<CombatCleanupProvider>.Instance),
            new FeeshProvider(Feesh, Fishing),
            new ArtisanProvider(Artisan, Crafting, Condition, NullLogger<ArtisanProvider>.Instance),
            _dutyProvider,
            new GatherChillProvider(Gather, Gathering),
            new KnightshopperProvider(Knightshopper, Purchase, GilShop),
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
        Feesh.Dispose();
    }
}
