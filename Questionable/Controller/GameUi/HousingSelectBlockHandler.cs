using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Handles the in-game "HousingSelectBlock" addon by confirming the selected housing ward.
/// </summary>
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

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "HousingSelectBlock", HousingSelectBlockPostSetup);
    }

    public void Dispose() =>
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "HousingSelectBlock", HousingSelectBlockPostSetup);

    private bool ShouldHandleUiInteractions =>
        _questController.IsRunning ||
        _territoryData.IsQuestBattleInstance(_clientState.TerritoryType);

    private unsafe void HousingSelectBlockPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        _logger.LogInformation("Confirming selected housing ward");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->FireCallbackInt(0);
    }
}
