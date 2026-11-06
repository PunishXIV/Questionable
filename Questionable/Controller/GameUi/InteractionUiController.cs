using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class InteractionUiController : IDisposable
{
    private readonly QuestController _questController;
    private readonly DialogueChoiceHandler _dialogueChoiceHandler;
    private readonly YesNoChoiceHandler _yesNoChoiceHandler;
    private readonly PointMenuHandler _pointMenuHandler;
    private readonly IFramework _framework;
    private StartupDialogueRecovery? _startupRecovery;

    public InteractionUiController(
        QuestController questController,
        IGameGuiAdapter gameGui,
        DialogueChoiceHandler dialogueChoiceHandler,
        YesNoChoiceHandler yesNoChoiceHandler,
        PointMenuHandler pointMenuHandler,
        IFramework framework)
    {
        _questController = questController;
        _dialogueChoiceHandler = dialogueChoiceHandler;
        _yesNoChoiceHandler = yesNoChoiceHandler;
        _pointMenuHandler = pointMenuHandler;
        _framework = framework;

        _questController.AutomationTypeChanged += OnAutomationTypeChanged;
        _framework.Update += OnFrameworkUpdate;

        unsafe
        {
            if (gameGui.TryGetAddonByName("RhythmAction", out AtkUnitBase* addon))
                addon->Close(fireCallback: true);
        }
    }

    public void Dispose()
    {
        _startupRecovery = null;
        _framework.Update -= OnFrameworkUpdate;
        _questController.AutomationTypeChanged -= OnAutomationTypeChanged;
    }

    private void OnAutomationTypeChanged(object sender, QuestController.EAutomationType automationType)
    {
        _startupRecovery = automationType == QuestController.EAutomationType.Manual
            ? null : new StartupDialogueRecovery(DateTime.UtcNow);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_startupRecovery is not { } recovery)
            return;

        if (recovery.ShouldCheck(DateTime.UtcNow, _questController.CurrentQuest != null,
                _questController.IsRunning, _questController.TaskQueue.CurrentTaskExecutor != null,
                _questController.HasExternalOperation))
        {
            if (!recovery.InitialSweepDone)
            {
                recovery.InitialSweepDone = true;
                HandleCurrentDialogueChoices();
                return;
            }
            if (_yesNoChoiceHandler.TryHandleStartupDialogue())
                _startupRecovery = null;
        }

        if (recovery.Finished && ReferenceEquals(_startupRecovery, recovery))
            _startupRecovery = null;
    }

    internal sealed class StartupDialogueRecovery(DateTime startedAt)
    {
        private readonly DateTime _expiresAt = startedAt.AddMinutes(2);
        private DateTime _nextCheckAt = startedAt;
        public bool Finished { get; private set; }
        public bool InitialSweepDone { get; set; }

        public bool ShouldCheck(DateTime now, bool hasQuest, bool hasTasks, bool executorStarted, bool externalOperation)
        {
            if (Finished || now >= _expiresAt || (executorStarted && InitialSweepDone))
            {
                Finished = true;
                return false;
            }
            if (!hasQuest || !hasTasks || externalOperation || now < _nextCheckAt)
                return false;

            _nextCheckAt = now.AddMilliseconds(500);
            return true;
        }
    }

    internal void HandleCurrentDialogueChoices()
    {
        _dialogueChoiceHandler.CheckExisting();
        _yesNoChoiceHandler.CheckExisting();
        _pointMenuHandler.CheckExisting();
    }
}
