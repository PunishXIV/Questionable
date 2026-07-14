using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Domain;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model.Questing;
using Questionable.Utils;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Handles the in-game "SelectYesno" and "DifficultySelectYesNo" addons — answering quest
///     dialogue prompts, travel confirmations, HQ-trade prompts and quest-battle difficulty selection.
/// </summary>
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
    private readonly ShopController _shopController;
    private readonly GrandCompanyExchangeController _grandCompanyExchangeController;
    private readonly ChocoboNamingController _chocoboNamingController;
    private readonly Configuration _configuration;
    private readonly IChatGui _chatGui;
    private readonly IToastGui _toastGui;
    private readonly DialogueReferenceResolver _dialogueReferenceResolver;
    private readonly TravelDestinationResolver _travelDestinationResolver;
    private readonly ILogger<YesNoChoiceHandler> _logger;

    private readonly Regex _returnRegex;
    private readonly Regex _purchaseItemRegex;
    private readonly Regex _ticketRegex;
    private readonly Regex _hqTradeRegex;

    public YesNoChoiceHandler(
        IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGui,
        QuestController questController,
        AetheryteFunctions aetheryteFunctions,
        BossModIpc bossModIpc,
        TerritoryData territoryData,
        IClientState clientState,
        ITargetManager targetManager,
        ShopController shopController,
        GrandCompanyExchangeController grandCompanyExchangeController,
        ChocoboNamingController chocoboNamingController,
        Configuration configuration,
        IChatGui chatGui,
        IToastGui toastGui,
        IDataManager dataManager,
        IPluginLog pluginLog,
        DialogueReferenceResolver dialogueReferenceResolver,
        TravelDestinationResolver travelDestinationResolver,
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
        _shopController = shopController;
        _grandCompanyExchangeController = grandCompanyExchangeController;
        _chocoboNamingController = chocoboNamingController;
        _configuration = configuration;
        _chatGui = chatGui;
        _toastGui = toastGui;
        _dialogueReferenceResolver = dialogueReferenceResolver;
        _travelDestinationResolver = travelDestinationResolver;
        _logger = logger;

        _returnRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 196, addon => addon.Text, pluginLog)!;
        _purchaseItemRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 3406, addon => addon.Text, pluginLog)!;
        _ticketRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 102686, addon => addon.Text, pluginLog)!;
        _hqTradeRegex = DataManagerAdapter.GetRegex<Addon>(dataManager, 102434, addon => addon.Text, pluginLog)!;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesnoPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "DifficultySelectYesNo", DifficultySelectYesNoPostSetup);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "DifficultySelectYesNo", DifficultySelectYesNoPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesnoPostSetup);
    }

    private bool ShouldHandleUiInteractions =>
        _questController.IsRunning ||
        _territoryData.IsQuestBattleInstance(_clientState.TerritoryType);

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

    /// <summary>
    ///     Handles any SelectYesno / DifficultySelectYesNo addon already open when automation starts.
    ///     Bypasses the <see cref="ShouldHandleUiInteractions"/> gate for SelectYesno, matching the
    ///     legacy "initial check" behaviour.
    /// </summary>
    public unsafe void CheckExisting()
    {
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
        if (_shopController.IsAwaitingYesNo && _purchaseItemRegex.IsMatch(actualPrompt))
        {
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            _shopController.IsAwaitingYesNo = false;
            return;
        }

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
            QuestSequence? sequence = quest.FindSequence(currentQuest.Sequence);
            if (sequence != null &&
                sequence.Steps.Any(step => HandleDefaultYesNo(addonSelectYesno, quest, step, step.DialogueChoices, actualPrompt)))
                return true;
        }
        else
        {
            QuestStep? step = quest.FindSequence(currentQuest.Sequence)?.FindStep(currentQuest.Step);
            if (step != null && HandleDefaultYesNo(addonSelectYesno, quest, step, step.DialogueChoices, actualPrompt))
                return true;
        }

        if (HandleTravelYesNo(addonSelectYesno, currentQuest, actualPrompt))
            return true;

        return false;
    }

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
            if (_configuration.General.DontSkipCutscenes && !_configuration.General.DontShowAnswerSuggestions)
                _toastGui.ShowQuest(notif);
            _chatGui.Print(notif, CommandHandler.MessageTag, CommandHandler.TagColor);
            _logger.LogInformation("Returning {YesNo} for '{Prompt}'", dialogueChoice.Yes ? "Yes" : "No", actualPrompt);
            if (!_configuration.General.DontSkipCutscenes)
                addonSelectYesno->AtkUnitBase.FireCallbackInt(dialogueChoice.Yes ? 0 : 1);
            return true;
        }

        if (CheckSinglePlayerDutyYesNo(quest.Id, step))
        {
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return true;
        }

        return false;
    }

    private bool CheckSinglePlayerDutyYesNo(ElementId questId, QuestStep? step)
    {
        if (step is { InteractionType: EInteractionType.SinglePlayerDuty } &&
            _bossModIpc.IsConfiguredToRunSoloInstance(questId, step.SinglePlayerDutyOptions))
        {
            // Most of these are yes/no dialogs "Duty calls, ...".
            //
            // For 'Vows of Virtue, Deeds of Cruelty', there's no such dialog, and it just puts you into the instance
            // after you confirm 'Wait for Krile?'. However, if you fail that duty, you'll get a DifficultySelectYesNo.

            // DifficultySelectYesNo → [0, 2] for very easy
            _logger.LogInformation("SinglePlayerDutyYesNo: probably Single Player Duty");
            return true;
        }

        return false;
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
        if (!_questController.IsRunning)
            return;

        QuestController.QuestProgress? currentQuest = _questController.StartedQuest;
        if (currentQuest == null)
            return;

        Quest quest = currentQuest.Quest;
        bool autoConfirm;
        if (checkAllSteps)
        {
            QuestSequence? sequence = quest.FindSequence(currentQuest.Sequence);
            autoConfirm = sequence != null && sequence.Steps.Any(step => CheckSinglePlayerDutyYesNo(quest.Id, step));
        }
        else
        {
            QuestStep? step = quest.FindSequence(currentQuest.Sequence)?.FindStep(currentQuest.Step);
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
