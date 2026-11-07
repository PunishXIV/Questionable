using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class YesNoChoiceHandler : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGuiAdapter _gameGui;
    private readonly QuestController _questController;
    private readonly AetheryteFunctions _aetheryteFunctions;
    private readonly BossModIpc _bossModIpc;
    private readonly TerritoryData _territoryData;
    private readonly IClientState _clientState;
    private readonly ITargetManager _targetManager;
    private readonly GrandCompanyExchangeController _grandCompanyExchangeController;
    private readonly ChocoboNamingController _chocoboNamingController;
    private readonly Configuration _configuration;
    private readonly IChatGui _chatGui;
    private readonly IToastGui _toastGui;
    private readonly DialogueReferenceResolver _dialogueReferenceResolver;
    private readonly TravelDestinationResolver _travelDestinationResolver;
    private readonly IFramework _framework;
    private readonly ICondition _condition;
    private readonly ILogger<YesNoChoiceHandler> _logger;

    private readonly Regex _returnRegex;
    private readonly Regex _ticketRegex;
    private readonly Regex _hqTradeRegex;
    private readonly Regex _fcInviteRegex;
    private readonly Regex[] _overCapRegexes;
    private SoloDutyConfirmation? _soloDutyConfirmation;

    public YesNoChoiceHandler(
        IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGui,
        QuestController questController,
        AetheryteFunctions aetheryteFunctions,
        BossModIpc bossModIpc,
        TerritoryData territoryData,
        IClientState clientState,
        ITargetManager targetManager,
        GrandCompanyExchangeController grandCompanyExchangeController,
        ChocoboNamingController chocoboNamingController,
        Configuration configuration,
        IChatGui chatGui,
        IToastGui toastGui,
        IDataManager dataManager,
        IPluginLog pluginLog,
        DialogueReferenceResolver dialogueReferenceResolver,
        TravelDestinationResolver travelDestinationResolver,
        IFramework framework,
        ICondition condition,
        ILogger<YesNoChoiceHandler> logger)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _questController = questController;
        _aetheryteFunctions = aetheryteFunctions;
        _bossModIpc = bossModIpc;
        _territoryData = territoryData;
        _clientState = clientState;
        _targetManager = targetManager;
        _grandCompanyExchangeController = grandCompanyExchangeController;
        _chocoboNamingController = chocoboNamingController;
        _configuration = configuration;
        _chatGui = chatGui;
        _toastGui = toastGui;
        _dialogueReferenceResolver = dialogueReferenceResolver;
        _travelDestinationResolver = travelDestinationResolver;
        _framework = framework;
        _condition = condition;
        _logger = logger;

        _returnRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 196, addon => addon.Text, pluginLog)!;
        _ticketRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 102686, addon => addon.Text, pluginLog)!;
        _hqTradeRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 102434, addon => addon.Text, pluginLog)!;
        _fcInviteRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 2873, addon => addon.Text, pluginLog)!;
        // Gil, company seals, tomestones and their combinations.
        _overCapRegexes = new uint[] { 4474, 4605, 4606, 4607, 4609, 5648, 5841 }
            .Select(row => DataManagerAdapter.GetRegex<Addon>(dataManager, row, addon => addon.Text, pluginLog))
            .OfType<Regex>()
            .ToArray();

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesnoPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "DifficultySelectYesNo", DifficultySelectYesNoPostSetup);
        _framework.Update += ConfirmNormalIfDutyDidNotStart;
        _questController.AutomationTypeChanged += OnAutomationTypeChanged;
    }

    public void Dispose()
    {
        _soloDutyConfirmation = null;
        _framework.Update -= ConfirmNormalIfDutyDidNotStart;
        _questController.AutomationTypeChanged -= OnAutomationTypeChanged;
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "DifficultySelectYesNo", DifficultySelectYesNoPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesnoPostSetup);
    }

    private bool ShouldHandleUiInteractions =>
        !_questController.HasExternalOperation &&
        (_questController.IsRunning || _territoryData.IsQuestBattleInstance(_clientState.TerritoryType));

    private unsafe void SelectYesnoPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        HandleSelectYesno((AddonSelectYesno*)args.Addon.Address, checkAllSteps: false);
    }

    private unsafe void DifficultySelectYesNoPostSetup(AddonEvent type, AddonArgs args)
    {
        HandleDifficultySelectYesNo((AtkUnitBase*)args.Addon.Address, checkAllSteps: false);
    }

    // The initial sweep bypasses the interaction gate.
    public unsafe void CheckExisting()
    {
        if (_questController.HasExternalOperation)
            return;

        if (_gameGui.TryGetAddonByName("SelectYesno", out AddonSelectYesno* addonSelectYesno))
        {
            _logger.LogInformation("SelectYesno window is open");
            HandleSelectYesno(addonSelectYesno, checkAllSteps: true);
        }

        if (_gameGui.TryGetAddonByName("DifficultySelectYesNo", out AtkUnitBase* addonDifficultySelectYesNo))
        {
            _logger.LogInformation("DifficultySelectYesNo window is open");
            HandleDifficultySelectYesNo(addonDifficultySelectYesNo, checkAllSteps: true);
        }
    }

    private unsafe void HandleSelectYesno(AddonSelectYesno* addonSelectYesno, bool checkAllSteps)
    {
        string? actualPrompt = AtkValueAdapter.ReadString(addonSelectYesno->AtkUnitBase.AtkValues[0]);
        if (actualPrompt == null)
            return;

        _logger.LogTrace("Prompt: '{Prompt}'", actualPrompt);
        if (_grandCompanyExchangeController.IsAwaitingYesNo)
        {
            _logger.LogInformation("Confirming Grand Company exchange purchase");
            new AddonMaster.SelectYesno(addonSelectYesno).Yes();
            _grandCompanyExchangeController.IsAwaitingYesNo = false;
            return;
        }

        if (_chocoboNamingController.IsAwaitingYesNo)
        {
            _logger.LogInformation("Confirming chocobo name");
            new AddonMaster.SelectYesno(addonSelectYesno).Yes();
            _chocoboNamingController.IsAwaitingYesNo = false;
            return;
        }

        QuestController.QuestProgress? currentQuest = _questController.StartedQuest;
        if (currentQuest != null && CheckQuestYesNo(addonSelectYesno, currentQuest, actualPrompt, checkAllSteps))
            return;

        QuestController.QuestProgress? simulatedQuest = _questController.SimulatedQuest;
        if (simulatedQuest != null && HandleTravelYesNo(addonSelectYesno, simulatedQuest, actualPrompt))
            return;

        QuestController.QuestProgress? nextQuest = _questController.NextQuest;
        if (nextQuest != null && CheckQuestYesNo(addonSelectYesno, nextQuest, actualPrompt, checkAllSteps))
            return;
    }

    private unsafe bool CheckQuestYesNo(AddonSelectYesno* addonSelectYesno, QuestController.QuestProgress currentQuest,
        string actualPrompt, bool checkAllSteps)
    {
        Quest quest = currentQuest.Quest;
        if (checkAllSteps)
        {
            if (HandleExplicitQuestYesNo(addonSelectYesno, currentQuest, actualPrompt))
                return true;
        }

        QuestStep? step = currentQuest.FindStep();
        if (step != null && HandleDefaultYesNo(addonSelectYesno, quest, step, step.DialogueChoices, actualPrompt))
            return true;

        if (HandleTravelYesNo(addonSelectYesno, currentQuest, actualPrompt))
            return true;

        return false;
    }

    // After a restart the player can already be in a later step's dialogue; only answer an explicit matching choice.
    internal unsafe bool TryHandleStartupDialogue()
    {
        if (_questController.HasExternalOperation || _questController.StartedQuest is not { } quest ||
            !_gameGui.TryGetAddonByName("SelectYesno", out AddonSelectYesno* addon) ||
            !addon->AtkUnitBase.IsVisible || !new AddonMaster.SelectYesno(addon).IsAddonReady ||
            addon->AtkUnitBase.AtkValues == null || addon->AtkUnitBase.AtkValuesCount == 0 ||
            addon->YesButton == null || addon->YesButton->AtkComponentBase.OwnerNode == null || !addon->YesButton->IsEnabled)
            return false;

        string? prompt = AtkValueAdapter.ReadString(addon->AtkUnitBase.AtkValues[0]);
        return prompt != null && HandleExplicitQuestYesNo(addon, quest, prompt);
    }

    private unsafe bool HandleExplicitQuestYesNo(AddonSelectYesno* addon, QuestController.QuestProgress progress, string prompt)
    {
        if (progress.FindSequence() is not { } sequence)
            return false;

        foreach (DialogueChoice choice in ExplicitYesNoChoices(sequence))
        {
            StringOrRegex? expected = _dialogueReferenceResolver.Resolve(progress.Quest, choice.ExcelSheet,
                choice.Prompt, choice.PromptIsRegularExpression);
            if (expected != null && DialogueReferenceResolver.IsMatch(prompt, expected) &&
                HandleDefaultYesNo(addon, progress.Quest, null, [choice], prompt))
                return true;
        }
        return false;
    }

    internal static IEnumerable<DialogueChoice> ExplicitYesNoChoices(QuestSequence sequence) =>
        sequence.Steps.SelectMany(step => step.DialogueChoices)
            .Where(choice => choice.Type == EDialogChoiceType.YesNo && choice.Prompt != null);

    private unsafe bool HandleDefaultYesNo(AddonSelectYesno* addonSelectYesno, Quest quest,
        QuestStep? step, List<DialogueChoice> dialogueChoices, string actualPrompt)
    {
        if (step is { InteractionType: EInteractionType.RegisterFreeOrFavoredAetheryte, Aetheryte: { } aetheryteLocation })
        {
            AetheryteRegistrationResult registrationResult = _aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(aetheryteLocation);
            if (registrationResult == AetheryteRegistrationResult.SecurityTokenFreeDestinationAvailable)
            {
                dialogueChoices =
                [
                    ..dialogueChoices,
                    new()
                    {
                        Type = EDialogChoiceType.YesNo,
                        ExcelSheet = "Addon",
                        Prompt = ExcelRef.FromRowId(102334),
                        Yes = true
                    }
                ];
            }
            else if (registrationResult == AetheryteRegistrationResult.FavoredDestinationAvailable)
            {
                dialogueChoices =
                [
                    ..dialogueChoices,
                    new()
                    {
                        Type = EDialogChoiceType.YesNo,
                        ExcelSheet = "Addon",
                        Prompt = ExcelRef.FromRowId(102306),
                        Yes = true
                    }
                ];
            }
        }

        _logger.LogTrace("DefaultYesNo: Choice count: {Count}", dialogueChoices.Count);
        foreach (DialogueChoice dialogueChoice in dialogueChoices)
        {
            if (dialogueChoice.Type != EDialogChoiceType.YesNo)
                continue;

            //force Yes if prompt is set to `null` while an answer is still given - basically means no key is available on sheets yet
            if (dialogueChoice.Prompt == null && dialogueChoice.Yes)
            {
                _logger.LogInformation("Forcing Yes because no key for this prompt is currently available.");
                addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
                return true;
            }

            if (dialogueChoice.DataId != null && dialogueChoice.DataId != GameFunctions.GetBaseID(_targetManager.Target))
            {
                _logger.LogDebug(
                    "Skipping entry in DialogueChoice expecting target dataId {ExpectedDataId}, actual target is {ActualTargetId}",
                    dialogueChoice.DataId, GameFunctions.GetBaseID(_targetManager.Target));
                continue;
            }

            StringOrRegex? excelPrompt = _dialogueReferenceResolver.Resolve(quest, dialogueChoice.ExcelSheet, dialogueChoice.Prompt,
                dialogueChoice.PromptIsRegularExpression);
            if (excelPrompt == null || !DialogueReferenceResolver.IsMatch(actualPrompt, excelPrompt))
            {
                _logger.LogInformation("Unexpected excelPrompt: {ExcelPrompt}, actualPrompt: {ActualPrompt}",
                    excelPrompt, actualPrompt);
                continue;
            }

            string notif = $"Question: '{actualPrompt}', {(_configuration.General.DontSkipCutscenes ? "scripted:" : "answering")} {(dialogueChoice.Yes ? "Yes" : "No")}";
            if (!_configuration.General.DontShowAnswerSuggestions)
            {
                if (_configuration.General.DontSkipCutscenes)
                    _toastGui.ShowQuest(notif);
                _chatGui.Print(notif, CommandHandler.MessageTag, CommandHandler.TagColor);
            }
            _logger.LogInformation("Returning {YesNo} for '{Prompt}'", dialogueChoice.Yes ? "Yes" : "No", actualPrompt);
            if (!_configuration.General.DontSkipCutscenes)
                addonSelectYesno->AtkUnitBase.FireCallbackInt(dialogueChoice.Yes ? 0 : 1);
            return true;
        }

        if (CheckSinglePlayerDutyYesNo(quest.Id, step))
        {
            ConfirmSinglePlayerDutyStart(addonSelectYesno, quest, step!, actualPrompt);
            return true;
        }

        return false;
    }

    private bool CheckSinglePlayerDutyYesNo(ElementId questId, QuestStep? step)
    {
        if (step is { InteractionType: EInteractionType.SinglePlayerDuty } &&
            _bossModIpc.CanRunQuestBattle(questId, step.SinglePlayerDutyOptions))
        {
            // "Duty calls" yes/no. After a wipe the game uses DifficultySelectYesNo instead.
            _logger.LogInformation("SinglePlayerDutyYesNo: probably Single Player Duty");
            return true;
        }

        return false;
    }

    // 823 requests the configured difficulty. Click Yes (Normal) if the duty does not start.
    private unsafe void ConfirmSinglePlayerDutyStart(AddonSelectYesno* addonSelectYesno, Quest quest,
        QuestStep step, string actualPrompt)
    {
        QuestController.QuestProgress? currentQuest = _questController.StartedQuest;
        if (currentQuest == null || currentQuest.Quest.Id != quest.Id || currentQuest.FindStep() != step)
            return;
        if (_soloDutyConfirmation is { } pending && pending.IsCurrent(currentQuest) && pending.Prompt == actualPrompt)
            return;

        _soloDutyConfirmation = null;
        byte difficulty = _configuration.SinglePlayerDuties.RetryDifficulty;
        _logger.LogInformation("Requesting solo quest battle at difficulty {Difficulty}", difficulty);

        if (!GameMain.ExecuteCommand((int)GameCommand.StartSoloQuestBattle, difficulty))
        {
            _logger.LogInformation("StartSoloQuestBattle was rejected; confirming Yes (Normal)");
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return;
        }

        _soloDutyConfirmation = new SoloDutyConfirmation(currentQuest, actualPrompt, DateTime.UtcNow);
    }

    private void OnAutomationTypeChanged(object sender, QuestController.EAutomationType automationType)
    {
        if (automationType == QuestController.EAutomationType.Manual)
            _soloDutyConfirmation = null;
    }

    private unsafe void ConfirmNormalIfDutyDidNotStart(IFramework framework)
    {
        if (_soloDutyConfirmation is not { } confirmation)
            return;

        QuestController.QuestProgress? currentQuest = _questController.StartedQuest;
        if (!_questController.IsRunning || _questController.HasExternalOperation ||
            _condition[ConditionFlag.BoundByDuty] || !confirmation.IsCurrent(currentQuest))
        {
            _soloDutyConfirmation = null;
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (now < confirmation.NextCheckAt)
            return;

        // The duty prompt can keep OccupiedInCutSceneEvent set, so only loading blocks the confirmation.
        bool loading = _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];
        string? prompt = null;
        bool ready = false;
        if (_gameGui.TryGetAddonByName("SelectYesno", out AddonSelectYesno* addon))
        {
            if (addon->AtkUnitBase.AtkValues != null && addon->AtkUnitBase.AtkValuesCount > 0)
                prompt = AtkValueAdapter.ReadString(addon->AtkUnitBase.AtkValues[0]);
            ready = addon->AtkUnitBase.IsVisible && new AddonMaster.SelectYesno(addon).IsAddonReady &&
                addon->YesButton != null && addon->YesButton->AtkComponentBase.OwnerNode != null &&
                addon->YesButton->IsEnabled;
        }

        switch (confirmation.Check(now, currentQuest, prompt, ready, loading))
        {
            case SoloDutyConfirmation.Result.Wait:
                return;
            case SoloDutyConfirmation.Result.Confirm:
                _soloDutyConfirmation = null;
                _logger.LogInformation("StartSoloQuestBattle did not enter the duty; confirming Yes (Normal)");
                addon->AtkUnitBase.FireCallbackInt(0);
                return;
            default:
                _soloDutyConfirmation = null;
                _logger.LogInformation("Solo duty confirmation expired or its quest/dialog changed; leaving the dialog untouched");
                return;
        }
    }

    // Keep the fallback across cutscenes without retaining an addon pointer or replaying command 823.
    internal sealed class SoloDutyConfirmation(QuestController.QuestProgress quest, string prompt, DateTime requestedAt)
    {
        internal enum Result
        {
            Wait,
            Confirm,
            Cancel
        }

        private readonly QuestController.StepProgress _step = quest.StepProgress;
        private readonly DateTime _expiresAt = requestedAt.AddMinutes(2);
        private bool _finished;
        public string Prompt { get; } = prompt;
        public DateTime NextCheckAt { get; private set; } = requestedAt.AddSeconds(2);

        public bool IsCurrent(QuestController.QuestProgress? current) =>
            current != null && ReferenceEquals(current, quest) && ReferenceEquals(current.StepProgress, _step);

        public Result Check(DateTime now, QuestController.QuestProgress? current, string? actualPrompt, bool ready, bool loading)
        {
            if (_finished || now >= _expiresAt || !IsCurrent(current) ||
                (actualPrompt != null && !string.Equals(actualPrompt, Prompt, StringComparison.Ordinal)))
            {
                _finished = true;
                return Result.Cancel;
            }
            if (now < NextCheckAt)
                return Result.Wait;

            NextCheckAt = now.AddMilliseconds(500);
            if (loading || !ready || actualPrompt == null)
                return Result.Wait;

            _finished = true;
            return Result.Confirm;
        }
    }

    private unsafe bool HandleTravelYesNo(AddonSelectYesno* addonSelectYesno,
        QuestController.QuestProgress currentQuest, string actualPrompt)
    {
        _logger.LogInformation("TravelYesNo");
        if (_aetheryteFunctions.ReturnRequestedAt >= DateTime.Now.AddSeconds(-2) && _returnRegex.IsMatch(actualPrompt))
        {
            _logger.LogInformation("Automatically confirming return...");
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return true;
        }

        if (_ticketRegex.IsMatch(actualPrompt))
        {
            _logger.LogInformation("Check UseTickets: {UseTickets}", _configuration.General.UseTickets);
            addonSelectYesno->AtkUnitBase.FireCallbackInt(_configuration.General.UseTickets ? 0 : 1);
            return true;
        }

        if (_questController.IsRunning && _hqTradeRegex != null && _hqTradeRegex.IsMatch(actualPrompt))
        {
            _logger.LogInformation("Automatically confirming HQ item trade");
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return true;
        }

        if (_questController.IsRunning && _overCapRegexes.Any(regex => regex.IsMatch(actualPrompt)))
        {
            if (_configuration.General.ConfirmCurrencyOverCap)
            {
                _logger.LogInformation("Confirming reward over the currency cap: {Prompt}", actualPrompt);
                _chatGui.Print(_L("Confirmed going over a currency cap; the excess was discarded."), CommandHandler.MessageTag, CommandHandler.TagColor);
                addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            }
            else
            {
                _logger.LogInformation("Reward would go over the currency cap, stopping: {Prompt}", actualPrompt);
                _chatGui.PrintError(_L("Stopped: this reward would go over a currency cap. Spend some first, or allow it in the settings."), CommandHandler.MessageTag, CommandHandler.TagColor);
                _questController.Stop("Currency over cap");
            }

            return true;
        }

        if (_questController.IsRunning && _gameGui.TryGetAddonByName("HousingSelectBlock", out AtkUnitBase* _))
        {
            _logger.LogInformation("Automatically confirming ward selection");
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return true;
        }

        ushort? targetTerritoryId = _travelDestinationResolver.FindTargetTerritoryFromQuestStep(currentQuest);
        if (targetTerritoryId != null &&
            _travelDestinationResolver.TryFindWarp(targetTerritoryId.Value, actualPrompt, out uint? warpId, out string? warpText))
        {
            _logger.LogInformation("Using warp {Id}, {Prompt}", warpId, warpText);
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return true;
        }

        return false;
    }

    private unsafe void HandleDifficultySelectYesNo(AtkUnitBase* addonDifficultySelectYesNo, bool checkAllSteps)
    {
        if (!_questController.IsRunning || _questController.HasExternalOperation)
            return;

        QuestController.QuestProgress? currentQuest = _questController.StartedQuest;
        if (currentQuest == null)
            return;

        Quest quest = currentQuest.Quest;
        bool autoConfirm;
        if (checkAllSteps)
        {
            QuestSequence? sequence = currentQuest.FindSequence();
            autoConfirm = sequence != null && sequence.Steps.Any(step => CheckSinglePlayerDutyYesNo(quest.Id, step));
        }
        else
        {
            QuestStep? step = currentQuest.FindStep();
            autoConfirm = step != null && CheckSinglePlayerDutyYesNo(quest.Id, step);
        }

        if (autoConfirm)
        {
            _logger.LogInformation("Confirming difficulty ({Difficulty}) for quest battle", _configuration.SinglePlayerDuties.RetryDifficulty);
            AtkValue* selectChoice = stackalloc AtkValue[]
            {
                new() { Type = AtkValueType.Int, Int = 0 },
                new() { Type = AtkValueType.Int, Int = _configuration.SinglePlayerDuties.RetryDifficulty }
            };
            addonDifficultySelectYesNo->FireCallback(2, selectChoice);
        }
    }
}
