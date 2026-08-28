using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Model.Questing;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Handles the in-game "PointMenu" addon (used by quest steps that require picking a map point),
///     picking the choice configured on the current quest step.
/// </summary>
[RegisterSingleton]
internal sealed class PointMenuHandler : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGuiAdapter _gameGui;
    private readonly QuestController _questController;
    private readonly TerritoryData _territoryData;
    private readonly IClientState _clientState;
    private readonly ILogger<PointMenuHandler> _logger;

    public PointMenuHandler(
        IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGui,
        QuestController questController,
        TerritoryData territoryData,
        IClientState clientState,
        ILogger<PointMenuHandler> logger)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _questController = questController;
        _territoryData = territoryData;
        _clientState = clientState;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "PointMenu", PointMenuPostSetup);
    }

    public void Dispose() =>
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "PointMenu", PointMenuPostSetup);

    private bool ShouldHandleUiInteractions =>
        _questController.IsRunning ||
        _territoryData.IsQuestBattleInstance(_clientState.TerritoryType);

    private unsafe void PointMenuPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        HandlePointMenu((AtkUnitBase*)args.Addon.Address);
    }

    /// <summary>
    ///     Handles a PointMenu that is already open when automation starts. Unlike the live addon
    ///     callback this bypasses the <see cref="ShouldHandleUiInteractions"/> gate, matching the
    ///     legacy "initial check" behaviour.
    /// </summary>
    public unsafe void CheckExisting()
    {
        if (_gameGui.TryGetAddonByName("PointMenu", out AtkUnitBase* addonPointMenu))
        {
            _logger.LogInformation("PointMenu is open");
            HandlePointMenu(addonPointMenu);
        }
    }

    private unsafe void HandlePointMenu(AtkUnitBase* addonPointMenu)
    {
        QuestController.QuestProgress? currentQuest = _questController.StartedQuest;
        if (currentQuest == null)
        {
            _logger.LogInformation("Ignoring point menu, no active quest");
            return;
        }

        QuestSequence? sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
        if (sequence == null)
            return;

        QuestStep? step = sequence.FindStep(currentQuest.Step);
        if (step == null)
            return;

        if (step.PointMenuChoices.Count == 0)
        {
            _logger.LogWarning("No point menu choices");
            return;
        }

        int counter = currentQuest.StepProgress.PointMenuCounter;
        if (counter >= step.PointMenuChoices.Count)
        {
            _logger.LogWarning("No remaining point menu choices");
            return;
        }

        uint choice = step.PointMenuChoices[counter];

        _logger.LogInformation("Handling point menu, picking choice {Choice} (index = {Index})", choice, counter);
        AtkValue* selectChoice = stackalloc AtkValue[]
        {
            new() { Type = AtkValueType.Int, Int = 13 },
            new() { Type = AtkValueType.UInt, UInt = choice }
        };
        addonPointMenu->FireCallback(2, selectChoice);

        currentQuest.IncreasePointMenuCounter();
    }
}
