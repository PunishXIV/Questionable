using System;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Utils;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Coordinates the per-addon interaction handlers. When automation starts it sweeps any addons
///     that are already open; live addon events are handled by each handler's own listener.
/// </summary>
internal sealed class InteractionUiController : IDisposable
{
    private readonly QuestController _questController;
    private readonly DialogueChoiceHandler _dialogueChoiceHandler;
    private readonly YesNoChoiceHandler _yesNoChoiceHandler;
    private readonly PointMenuHandler _pointMenuHandler;

    public InteractionUiController(
        QuestController questController,
        IGameGuiAdapter gameGui,
        DialogueChoiceHandler dialogueChoiceHandler,
        YesNoChoiceHandler yesNoChoiceHandler,
        PointMenuHandler pointMenuHandler)
    {
        _questController = questController;
        _dialogueChoiceHandler = dialogueChoiceHandler;
        _yesNoChoiceHandler = yesNoChoiceHandler;
        _pointMenuHandler = pointMenuHandler;

        _questController.AutomationTypeChanged += OnAutomationTypeChanged;

        unsafe
        {
            if (gameGui.TryGetAddonByName("RhythmAction", out AtkUnitBase* addon))
                addon->Close(fireCallback: true);
        }
    }

    public void Dispose()
    {
        _questController.AutomationTypeChanged -= OnAutomationTypeChanged;
    }

    private void OnAutomationTypeChanged(object sender, QuestController.EAutomationType automationType)
    {
        if (automationType != QuestController.EAutomationType.Manual)
            HandleCurrentDialogueChoices();
    }

    /// <summary>
    ///     Sweeps every addon that may already be open when automation starts, letting each handler
    ///     act on it. Live addon events are handled by the individual handlers' own listeners.
    /// </summary>
    internal void HandleCurrentDialogueChoices()
    {
        _dialogueChoiceHandler.CheckExisting();
        _yesNoChoiceHandler.CheckExisting();
        _pointMenuHandler.CheckExisting();
    }
}
