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
        AutoDutyIpc autoDuty) =>
    [
        new KnightshopperOperationProvider(knightshopper, purchase, new GilShopData(Substitute.For<IDataManager>())),
        new GatherChillOperationProvider(gather, gathering),
        new FeeshOperationProvider(feesh, fishing),
        new AutoDutyOperationProvider(autoDuty),
    ];

    public static Lazy<ExternalOperationCoordinator> Coordinator(IEnumerable<IOperationProvider> providers,
        Func<MovementController> movement) =>
        new(() => new ExternalOperationCoordinator(providers, movement(),
            NullLogger<ExternalOperationCoordinator>.Instance));
}
