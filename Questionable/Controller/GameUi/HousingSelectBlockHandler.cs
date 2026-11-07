using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class HousingSelectBlockHandler : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly QuestController _questController;
    private readonly TerritoryData _territoryData;
    private readonly IClientState _clientState;
    private readonly ILogger<HousingSelectBlockHandler> _logger;

    public HousingSelectBlockHandler(
        IAddonLifecycle addonLifecycle,
        QuestController questController,
        TerritoryData territoryData,
        IClientState clientState,
        ILogger<HousingSelectBlockHandler> logger)
    {
        _addonLifecycle = addonLifecycle;
        _questController = questController;
        _territoryData = territoryData;
        _clientState = clientState;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "HousingSelectBlock", OnPostSetup);
    }

    public void Dispose() =>
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "HousingSelectBlock", OnPostSetup);

    private bool ShouldHandleUi =>
        _questController.IsRunning ||
        _territoryData.IsQuestBattleInstance(_clientState.TerritoryType);

    private unsafe void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUi)
            return;

        _logger.LogInformation("Confirming selected housing ward");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->FireCallbackInt(0);
    }
}
