using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class CreditsController : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly QuestController _questController;
    private readonly ILogger<CreditsController> _logger;

    public CreditsController(IAddonLifecycle addonLifecycle, QuestController questController,
        ILogger<CreditsController> logger)
    {
        _addonLifecycle = addonLifecycle;
        _questController = questController;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "CreditScroll", CreditScrollPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Credit", CreditPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "CreditPlayer", CreditPlayerPostSetup);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CreditPlayer", CreditPlayerPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Credit", CreditPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CreditScroll", CreditScrollPostSetup);
    }

    private bool IsQuesting =>
        _questController.IsRunning || _questController.AutomationType != QuestController.EAutomationType.Manual;

    private unsafe void CreditScrollPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!IsQuesting)
            return;

        _logger.LogInformation("Closing Credits sequence");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->FireCallbackInt(-2);
    }

    private unsafe void CreditPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!IsQuesting)
            return;

        _logger.LogInformation("Closing Credits sequence");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->FireCallbackInt(-2);
    }

    private unsafe void CreditPlayerPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!IsQuesting)
            return;

        _logger.LogInformation("Closing CreditPlayer");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->Close(fireCallback: true);
    }
}
