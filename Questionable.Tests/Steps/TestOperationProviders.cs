using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Operations;
using Questionable.Data;
using Questionable.External;

namespace Questionable.Tests.Steps;

internal static class TestOperationProviders
{
    public static IOperationProvider[] Moving(PurchaseOperationState purchase, IKnightshopperIpc knightshopper,
        GatheringOperationState gathering, IGatherChillIpc gather, FishingOperationState fishing, IFeeshIpc feesh,
        AutoDutyOperationProvider autoDuty) =>
    [
        new KnightshopperOperationProvider(knightshopper, purchase, new GilShopData(Substitute.For<IDataManager>())),
        new GatherChillOperationProvider(gather, gathering),
        new FeeshOperationProvider(feesh, fishing),
        autoDuty,
    ];

    // Shared fixtures only check busy status; they never start duties or look up territory data.
    public static AutoDutyOperationProvider AutoDuty(AutoDutyIpc ipc) =>
        new(ipc, new Configuration(), null!, NullLogger<AutoDutyOperationProvider>.Instance, new(),
            Substitute.For<IClientState>(), Substitute.For<ICondition>(), Substitute.For<IDutyState>(),
            new NavmeshIpc(Substitute.For<IDalamudPluginInterface>(), NullLogger<NavmeshIpc>.Instance));

    public static Lazy<ExternalOperationCoordinator> Coordinator(IEnumerable<IOperationProvider> providers,
        Func<MovementController> movement) =>
        new(() => new ExternalOperationCoordinator(providers, movement(),
            NullLogger<ExternalOperationCoordinator>.Instance));
}
