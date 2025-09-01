using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib;
using LLib.GameData;
using LLib.GameUI;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Quest = Questionable.Model.Quest;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Questionable.Controller.GameUi;

internal sealed class InteractionUiController : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IDataManager _dataManager;
    private readonly QuestFunctions _questFunctions;
    private readonly AetheryteFunctions _aetheryteFunctions;
    private readonly ExcelFunctions _excelFunctions;
    private readonly QuestController _questController;
    private readonly GatheringPointRegistry _gatheringPointRegistry;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestData _questData;
    private readonly TerritoryData _territoryData;
    private readonly IGameGui _gameGui;
    private readonly ITargetManager _targetManager;
    private readonly IClientState _clientState;
    private readonly ShopController _shopController;
    private readonly BossModIpc _bossModIpc;
    private readonly Configuration _configuration;
    private readonly ILogger<InteractionUiController> _logger;
    private readonly Regex _returnRegex;
    private readonly Regex _purchaseItemRegex;

    private bool _isInitialCheck;

    public InteractionUiController(
        IAddonLifecycle addonLifecycle,
        IDataManager dataManager,
        QuestFunctions questFunctions,
        AetheryteFunctions aetheryteFunctions,
        ExcelFunctions excelFunctions,
        QuestController questController,
        GatheringPointRegistry gatheringPointRegistry,
        QuestRegistry questRegistry,
        QuestData questData,
        TerritoryData territoryData,
        IGameGui gameGui,
        ITargetManager targetManager,
        IPluginLog pluginLog,
        IClientState clientState,
        ShopController shopController,
        BossModIpc bossModIpc,
        Configuration configuration,
        ILogger<InteractionUiController> logger)
    {
        _addonLifecycle = addonLifecycle;
        _dataManager = dataManager;
        _questFunctions = questFunctions;
        _aetheryteFunctions = aetheryteFunctions;
        _excelFunctions = excelFunctions;
        _questController = questController;
        _gatheringPointRegistry = gatheringPointRegistry;
        _questRegistry = questRegistry;
        _questData = questData;
        _territoryData = territoryData;
        _gameGui = gameGui;
        _targetManager = targetManager;
        _clientState = clientState;
        _shopController = shopController;
        _bossModIpc = bossModIpc;
        _configuration = configuration;
        _logger = logger;

        _returnRegex = _dataManager.GetExcelSheet<Addon>().GetRow(196).GetRegex(addon => addon.Text, pluginLog)!;
        _purchaseItemRegex = _dataManager.GetRegex<Addon>(3406, addon => addon.Text, pluginLog)!;

        _questController.AutomationTypeChanged += HandleCurrentDialogueChoices;
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", SelectStringPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "CutSceneSelectString", CutsceneSelectStringPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectIconString", SelectIconStringPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesnoPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "DifficultySelectYesNo", DifficultySelectYesNoPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "PointMenu", PointMenuPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "HousingSelectBlock", HousingSelectBlockPostSetup);

        unsafe
        {
            if (_gameGui.TryGetAddonByName("RhythmAction", out AtkUnitBase* addon))
            {
                addon->Close(true);
            }
        }
    }

    private bool ShouldHandleUiInteractions => _isInitialCheck ||
                                               _questController.IsRunning ||
                                               _territoryData.IsQuestBattleInstance(_clientState.TerritoryType);

    private void HandleCurrentDialogueChoices(object sender, QuestController.EAutomationType automationType)
    {
        if (automationType != QuestController.EAutomationType.Manual)
            HandleCurrentDialogueChoices();
    }

    internal unsafe void HandleCurrentDialogueChoices()
    {
        try
        {
            _isInitialCheck = true;
            if (_gameGui.TryGetAddonByName("SelectString", out AddonSelectString* addonSelectString))
            {
                _logger.LogInformation("SelectString window is open");
                SelectStringPostSetup(addonSelectString, true);
            }

            if (_gameGui.TryGetAddonByName("CutSceneSelectString",
                    out AddonCutSceneSelectString* addonCutSceneSelectString))
            {
                _logger.LogInformation("CutSceneSelectString window is open");
                CutsceneSelectStringPostSetup(addonCutSceneSelectString, true);
            }

            if (_gameGui.TryGetAddonByName("SelectIconString", out AddonSelectIconString* addonSelectIconString))
            {
                _logger.LogInformation("SelectIconString window is open");
                SelectIconStringPostSetup(addonSelectIconString, true);
            }

            if (_gameGui.TryGetAddonByName("SelectYesno", out AddonSelectYesno* addonSelectYesno))
            {
                _logger.LogInformation("SelectYesno window is open");
                SelectYesnoPostSetup(addonSelectYesno, true);
            }

            if (_gameGui.TryGetAddonByName("DifficultySelectYesNo", out AtkUnitBase* addonDifficultySelectYesNo))
            {
                _logger.LogInformation("DifficultySelectYesNo window is open");
                DifficultySelectYesNoPostSetup(addonDifficultySelectYesNo, true);
            }

            if (_gameGui.TryGetAddonByName("PointMenu", out AtkUnitBase* addonPointMenu))
            {
                _logger.LogInformation("PointMenu is open");
                PointMenuPostSetup(addonPointMenu);
            }
        }
        finally
        {
            _isInitialCheck = false;
        }
    }

    private unsafe void SelectStringPostSetup(AddonEvent type, AddonArgs args)
    {
        AddonSelectString* addonSelectString = (AddonSelectString*)args.Addon.Address;
        SelectStringPostSetup(addonSelectString, false);
    }

    private unsafe void SelectStringPostSetup(AddonSelectString* addonSelectString, bool checkAllSteps)
    {
        if (!ShouldHandleUiInteractions)
            return;

        string? actualPrompt = addonSelectString->AtkUnitBase.AtkValues[2].ReadAtkString();
        if (actualPrompt == null)
            return;

        List<string?> answers = new();
        for (ushort i = 7; i < addonSelectString->AtkUnitBase.AtkValuesCount; ++i)
        {
            if (addonSelectString->AtkUnitBase.AtkValues[i].Type == ValueType.String)
                answers.Add(addonSelectString->AtkUnitBase.AtkValues[i].ReadAtkString());
        }

        int? answer = HandleListChoice(actualPrompt, answers, checkAllSteps) ?? HandleInstanceListChoice(actualPrompt);
        if (answer != null)
        {
            _logger.LogInformation("Using choice {Choice} for list prompt '{Prompt}'", answer, actualPrompt);
            addonSelectString->AtkUnitBase.FireCallbackInt(answer.Value);
        }
    }

    private unsafe void CutsceneSelectStringPostSetup(AddonEvent type, AddonArgs args)
    {
        AddonCutSceneSelectString* addonCutSceneSelectString = (AddonCutSceneSelectString*)args.Addon.Address;
        CutsceneSelectStringPostSetup(addonCutSceneSelectString, false);
    }

    private unsafe void CutsceneSelectStringPostSetup(AddonCutSceneSelectString* addonCutSceneSelectString,
        bool checkAllSteps)
    {
        if (!ShouldHandleUiInteractions)
            return;

        string? actualPrompt = addonCutSceneSelectString->AtkUnitBase.AtkValues[2].ReadAtkString();
        if (actualPrompt == null)
            return;

        List<string?> answers = new();
        for (int i = 5; i < addonCutSceneSelectString->AtkUnitBase.AtkValuesCount; ++i)
            answers.Add(addonCutSceneSelectString->AtkUnitBase.AtkValues[i].ReadAtkString());

        int? answer = HandleListChoice(actualPrompt, answers, checkAllSteps);
        if (answer != null)
            addonCutSceneSelectString->AtkUnitBase.FireCallbackInt(answer.Value);
    }

    private unsafe void SelectIconStringPostSetup(AddonEvent type, AddonArgs args)
    {
        AddonSelectIconString* addonSelectIconString = (AddonSelectIconString*)args.Addon.Address;
        SelectIconStringPostSetup(addonSelectIconString, false);
    }

    [SuppressMessage("ReSharper", "RedundantJumpStatement")]
    private unsafe void SelectIconStringPostSetup(AddonSelectIconString* addonSelectIconString, bool checkAllSteps)
    {
        if (!ShouldHandleUiInteractions)
            return;

        string? actualPrompt = addonSelectIconString->AtkUnitBase.AtkValues[3].ReadAtkString();
        if (string.IsNullOrEmpty(actualPrompt))
            actualPrompt = null;

        var answers = GetChoices(addonSelectIconString);
        int? answer = HandleListChoice(actualPrompt, answers, checkAllSteps);
        if (answer != null)
        {
            _logger.LogInformation("Using choice {Choice} for list prompt '{Prompt}'", answer, actualPrompt);
            addonSelectIconString->AtkUnitBase.FireCallbackInt(answer.Value);
            return;
        }

        // this is 'Daily Quests' for tribal quests, but not set for normal selections
        string? title = addonSelectIconString->AtkValues[0].ReadAtkString();

        var currentQuest = _questController.StartedQuest;
        if (currentQuest != null && (actualPrompt == null || title != null))
        {
            _logger.LogInformation("Checking if current quest {Name} is on the list", currentQuest.Quest.Info.Name);
            if (CheckQuestSelection(addonSelectIconString, currentQuest.Quest, answers))
                return;

            var sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
            QuestStep? step = sequence?.FindStep(currentQuest.Step);
            if (step is { InteractionType: EInteractionType.AcceptQuest, PickUpQuestId: not null } &&
                _questRegistry.TryGetQuest(step.PickUpQuestId, out Quest? pickupQuest))
            {
                _logger.LogInformation("Checking if current picked-up {Name} is on the list", pickupQuest.Info.Name);
                if (CheckQuestSelection(addonSelectIconString, pickupQuest, answers))
                    return;
            }
        }

        var nextQuest = _questController.NextQuest;
        if (nextQuest != null && (actualPrompt == null || title != null))
        {
            _logger.LogInformation("Checking if next quest {Name} is on the list", nextQuest.Quest.Info.Name);
            if (CheckQuestSelection(addonSelectIconString, nextQuest.Quest, answers))
                return;
        }
    }

    private unsafe bool CheckQuestSelection(AddonSelectIconString* addonSelectIconString, Quest quest,
        List<string?> answers)
    {
        // it is possible for this to be a quest selection
        string questName = quest.Info.Name;
        int questSelection = answers.FindIndex(x => GameFunctions.GameStringEquals(questName, x));
        if (questSelection >= 0)
        {
            _logger.LogInformation("Selecting quest {QuestName}", questName);
            addonSelectIconString->AtkUnitBase.FireCallbackInt(questSelection);
            return true;
        }

        return false;
    }

    public static unsafe List<string?> GetChoices(AddonSelectIconString* addonSelectIconString)
    {
        List<string?> answers = new();
        for (ushort i = 0; i < addonSelectIconString->AtkUnitBase.AtkValues[5].Int; i++)
            answers.Add(addonSelectIconString->AtkValues[i * 3 + 7].ReadAtkString());

        return answers;
    }

    private int? HandleListChoice(string? actualPrompt, List<string?> answers, bool checkAllSteps)
    {
        List<DialogueChoiceInfo> dialogueChoices = [];

        var currentQuest = _questController.SimulatedQuest ??
                           _questController.GatheringQuest ??
                           _questController.StartedQuest;
        if (currentQuest != null)
        {
            var quest = currentQuest.Quest;
            bool isTaxiStandUnlock = false;
            List<EAetheryteLocation> freeOrFavoredAetheryteRegistrations = [];
            if (checkAllSteps)
            {
                var sequence = quest.FindSequence(currentQuest.Sequence);
                var choices = sequence?.Steps.SelectMany(x => x.DialogueChoices);
                if (choices != null)
                    dialogueChoices.AddRange(choices.Select(x => new DialogueChoiceInfo(quest, x)));

                isTaxiStandUnlock = sequence?.Steps.Any(x => x.InteractionType == EInteractionType.UnlockTaxiStand) ??
                                    false;
                freeOrFavoredAetheryteRegistrations = sequence?.Steps
                                                          .Where(x => x is
                                                          {
                                                              InteractionType: EInteractionType
                                                                  .RegisterFreeOrFavoredAetheryte,
                                                              Aetheryte: not null
                                                          })
                                                          .Select(x => x.Aetheryte!.Value).ToList()
                                                      ?? [];
            }
            else
            {
                QuestStep? step = null;
                if (_territoryData.IsQuestBattleInstance(_clientState.TerritoryType))
                {
                    step = quest.FindSequence(currentQuest.Sequence)?.Steps
                        .FirstOrDefault(x => x.InteractionType == EInteractionType.SinglePlayerDuty);
                }

                if (step == null)
                    step = quest.FindSequence(currentQuest.Sequence)?.FindStep(currentQuest.Step);

                if (step == null)
                    _logger.LogDebug("Ignoring current quest dialogue choices, no active step");
                else
                {
                    dialogueChoices.AddRange(step.DialogueChoices.Select(x => new DialogueChoiceInfo(quest, x)));
                    if (step.PurchaseMenu != null)
                        dialogueChoices.Add(new DialogueChoiceInfo(quest, new DialogueChoice
                        {
                            Type = EDialogChoiceType.List,
                            ExcelSheet = step.PurchaseMenu.ExcelSheet,
                            Prompt = null,
                            Answer = step.PurchaseMenu.Key,
                        }));

                    if (step is { InteractionType: EInteractionType.RegisterFreeOrFavoredAetheryte, Aetheryte: { } aetheryte })
                        freeOrFavoredAetheryteRegistrations = [aetheryte];

                    isTaxiStandUnlock = step.InteractionType == EInteractionType.UnlockTaxiStand;
                }
            }

            if (isTaxiStandUnlock)
            {
                _logger.LogInformation("Adding chocobo taxi stand unlock dialogue choices");
                dialogueChoices.Add(new DialogueChoiceInfo(quest, new DialogueChoice
                {
                    Type = EDialogChoiceType.List,
                    ExcelSheet = "transport/ChocoboTaxiStand",
                    Prompt = ExcelRef.FromKey("TEXT_CHOCOBOTAXISTAND_00000_Q1_000_1"),
                    Answer = ExcelRef.FromKey("TEXT_CHOCOBOTAXISTAND_00000_A1_000_3")
                }));
            }

            if (freeOrFavoredAetheryteRegistrations.Any(x =>
                    _aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(x) ==
                    AetheryteRegistrationResult.SecurityTokenFreeDestinationAvailable))
            {
                _logger.LogInformation("Adding security token aetheryte unlock dialogue choice");
                dialogueChoices.Add(new DialogueChoiceInfo(quest, new DialogueChoice
                {
                    Type = EDialogChoiceType.List,
                    ExcelSheet = "transport/Aetheryte",
                    Prompt = ExcelRef.FromKey("TEXT_AETHERYTE_MAINMENU_TITLE"),
                    PromptIsRegularExpression = true,
                    Answer = ExcelRef.FromKey("TEXT_AETHERYTE_REGISTER_TOKEN_FAVORITE"),
                    AnswerIsRegularExpression = true,
                }));
            }
            else if (freeOrFavoredAetheryteRegistrations.Any(x =>
                    _aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(x) ==
                    AetheryteRegistrationResult.FavoredDestinationAvailable))
            {
                _logger.LogInformation("Adding favored aetheryte unlock dialogue choice");
                dialogueChoices.Add(new DialogueChoiceInfo(quest, new DialogueChoice
                {
                    Type = EDialogChoiceType.List,
                    ExcelSheet = "transport/Aetheryte",
                    Prompt = ExcelRef.FromKey("TEXT_AETHERYTE_MAINMENU_TITLE"),
                    PromptIsRegularExpression = true,
                    Answer = ExcelRef.FromKey("TEXT_AETHERYTE_REGISTER_FAVORITE"),
                    AnswerIsRegularExpression = true,
                }));
            }

            // add all travel dialogue choices
            var targetTerritoryId = FindTargetTerritoryFromQuestStep(currentQuest);
            if (targetTerritoryId != null)
            {
                foreach (string? answer in answers)
                {
                    if (answer == null)
                        continue;

                    if (TryFindWarp(targetTerritoryId.Value, answer, out uint? warpId, out string? warpText))
                    {
                        _logger.LogInformation("Adding warp {Id}, {Prompt}", warpId, warpText);
                        dialogueChoices.Add(new DialogueChoiceInfo(quest, new DialogueChoice
                        {
                            Type = EDialogChoiceType.List,
                            ExcelSheet = null,
                            Prompt = null,
                            Answer = ExcelRef.FromSheetValue(warpText),
                        }));
                    }
                }
            }
        }
        else
            _logger.LogDebug("Ignoring current quest dialogue choices, no active quest");

        // add all quests that start with the targeted npc
        var target = _targetManager.Target;
        if (target != null)
        {
            foreach (var questInfo in _questData.GetAllByIssuerDataId(target.DataId).Where(x => x.QuestId is QuestId))
            {
                if (_questFunctions.IsReadyToAcceptQuest(questInfo.QuestId) &&
                    _questRegistry.TryGetQuest(questInfo.QuestId, out Quest? knownQuest))
                {
                    var questChoices = knownQuest.FindSequence(0)?.Steps
                        .SelectMany(x => x.DialogueChoices)
                        .ToList();
                    if (questChoices != null && questChoices.Count > 0)
                    {
                        _logger.LogInformation("Adding {Count} dialogue choices from not accepted quest {QuestName}",
                            questChoices.Count, questInfo.Name);
                        dialogueChoices.AddRange(questChoices.Select(x => new DialogueChoiceInfo(knownQuest, x)));
                    }
                }
            }
        }

        if (dialogueChoices.Count == 0)
        {
            _logger.LogDebug("No dialogue choices to check");
            return null;
        }

        foreach (var (quest, dialogueChoice) in dialogueChoices)
        {
            if (dialogueChoice.Type != EDialogChoiceType.List)
                continue;

            if (dialogueChoice.SpecialCondition == "NoDutyActions")
            {
                try
                {
                    unsafe
                    {
                        ContentDirector* contentDirector = EventFramework.Instance()->GetContentDirector();
                        if (contentDirector != null && contentDirector->DutyActionManager.ActionsPresent)
                        {
                            _logger.LogInformation("NoDutyActions: actions present, skipping dialogue choice");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check for duty actions");
                    continue;
                }
            }

            if (dialogueChoice.Answer == null)
            {
                _logger.LogDebug("Ignoring entry in DialogueChoices, no answer");
                continue;
            }

            if (dialogueChoice.DataId != null && dialogueChoice.DataId != _targetManager.Target?.DataId)
            {
                _logger.LogDebug(
                    "Skipping entry in DialogueChoice expecting target dataId {ExpectedDataId}, actual target is {ActualTargetId}",
                    dialogueChoice.DataId, _targetManager.Target?.DataId);
                continue;
            }

            StringOrRegex? excelPrompt = ResolveReference(quest, dialogueChoice.ExcelSheet, dialogueChoice.Prompt,
                    dialogueChoice.PromptIsRegularExpression);
            StringOrRegex? excelAnswer = ResolveReference(quest, dialogueChoice.ExcelSheet, dialogueChoice.Answer,
                dialogueChoice.AnswerIsRegularExpression);

            if (actualPrompt == null && excelPrompt != null)
            {
                _logger.LogInformation("Unexpected excelPrompt: {ExcelPrompt}", excelPrompt);
                continue;
            }

            if (actualPrompt != null &&
                (excelPrompt == null || !IsMatch(actualPrompt, excelPrompt)))
            {
                _logger.LogInformation("Unexpected excelPrompt: {ExcelPrompt}, actualPrompt: {ActualPrompt}",
                    excelPrompt, actualPrompt);
                continue;
            }

            for (int i = 0; i < answers.Count; ++i)
            {
                _logger.LogTrace("Checking if {ActualAnswer} == {ExpectedAnswer}",
                    answers[i], excelAnswer);
                if (IsMatch(answers[i], excelAnswer))
                {
                    _logger.LogInformation("Returning {Index}: '{Answer}' for '{Prompt}'",
                        i, answers[i], actualPrompt);

                    // ensure we only open the dialog once
                    if (quest?.Id is SatisfactionSupplyNpcId)
                    {
                        if (_questController.GatheringQuest == null ||
                            _questController.GatheringQuest.Sequence == 255)
                            return null;

                        _questController.GatheringQuest.SetSequence(1);
                        _questController.StartGatheringQuest("SatisfactionSupply turn in");
                    }

                    return i;
                }
            }
        }

        _logger.LogInformation("No matching answer found for {Prompt}.", actualPrompt);
        return null;
    }

    private static bool IsMatch(string? actualAnswer, StringOrRegex? expectedAnswer)
    {
        if (actualAnswer == null && expectedAnswer == null)
            return true;

        if (actualAnswer == null || expectedAnswer == null)
            return false;

        return expectedAnswer.IsMatch(actualAnswer);
    }

    private int? HandleInstanceListChoice(string? actualPrompt)
    {
        string? expectedPrompt = _excelFunctions.GetDialogueTextByRowId("Addon", 2090, false).GetString();
        if (GameFunctions.GameStringEquals(actualPrompt, expectedPrompt))
        {
            _logger.LogInformation("Selecting no prefered instance as answer for '{Prompt}'", actualPrompt);
            return 0; // any instance
        }

        return null;
    }

    private unsafe void SelectYesnoPostSetup(AddonEvent type, AddonArgs args)
    {
        AddonSelectYesno* addonSelectYesno = (AddonSelectYesno*)args.Addon.Address;
        SelectYesnoPostSetup(addonSelectYesno, false);
    }

    [SuppressMessage("ReSharper", "RedundantJumpStatement")]
    private unsafe void SelectYesnoPostSetup(AddonSelectYesno* addonSelectYesno, bool checkAllSteps)
    {
        if (!ShouldHandleUiInteractions)
            return;

        string? actualPrompt = addonSelectYesno->AtkUnitBase.AtkValues[0].ReadAtkString();
        if (actualPrompt == null)
            return;

        _logger.LogTrace("Prompt: '{Prompt}'", actualPrompt);
        if (_shopController.IsAwaitingYesNo && _purchaseItemRegex.IsMatch(actualPrompt))
        {
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            _shopController.IsAwaitingYesNo = false;
            return;
        }

        var currentQuest = _questController.StartedQuest;
        if (currentQuest != null && CheckQuestYesNo(addonSelectYesno, currentQuest, actualPrompt, checkAllSteps))
            return;

        var simulatedQuest = _questController.SimulatedQuest;
        if (simulatedQuest != null && HandleTravelYesNo(addonSelectYesno, simulatedQuest, actualPrompt))
            return;

        var nextQuest = _questController.NextQuest;
        if (nextQuest != null && CheckQuestYesNo(addonSelectYesno, nextQuest, actualPrompt, checkAllSteps))
            return;

        return;
    }

    private unsafe bool CheckQuestYesNo(AddonSelectYesno* addonSelectYesno, QuestController.QuestProgress currentQuest,
        string actualPrompt, bool checkAllSteps)
    {
        var quest = currentQuest.Quest;
        if (checkAllSteps)
        {
            var sequence = quest.FindSequence(currentQuest.Sequence);
            if (sequence != null &&
                sequence.Steps.Any(step => HandleDefaultYesNo(addonSelectYesno, quest, step, step.DialogueChoices, actualPrompt)))
                return true;
        }
        else
        {
            var step = quest.FindSequence(currentQuest.Sequence)?.FindStep(currentQuest.Step);
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
            var registrationResult = _aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(aetheryteLocation);
            if (registrationResult == AetheryteRegistrationResult.SecurityTokenFreeDestinationAvailable)
            {
                dialogueChoices =
                [
                    ..dialogueChoices,
                    new DialogueChoice
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
                    new DialogueChoice
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
        foreach (var dialogueChoice in dialogueChoices)
        {
            if (dialogueChoice.Type != EDialogChoiceType.YesNo)
                continue;

            if (dialogueChoice.DataId != null && dialogueChoice.DataId != _targetManager.Target?.DataId)
            {
                _logger.LogDebug(
                    "Skipping entry in DialogueChoice expecting target dataId {ExpectedDataId}, actual target is {ActualTargetId}",
                    dialogueChoice.DataId, _targetManager.Target?.DataId);
                continue;
            }

            StringOrRegex? excelPrompt = ResolveReference(quest, dialogueChoice.ExcelSheet, dialogueChoice.Prompt,
                    dialogueChoice.PromptIsRegularExpression);
            if (excelPrompt == null || !IsMatch(actualPrompt, excelPrompt))
            {
                _logger.LogInformation("Unexpected excelPrompt: {ExcelPrompt}, actualPrompt: {ActualPrompt}",
                    excelPrompt, actualPrompt);
                continue;
            }

            _logger.LogInformation("Returning {YesNo} for '{Prompt}'", dialogueChoice.Yes ? "Yes" : "No", actualPrompt);
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

        if (_questController.IsRunning && _gameGui.TryGetAddonByName("HousingSelectBlock", out AtkUnitBase* _))
        {
            _logger.LogInformation("Automatically confirming ward selection");
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return true;
        }

        var targetTerritoryId = FindTargetTerritoryFromQuestStep(currentQuest);
        if (targetTerritoryId != null &&
            TryFindWarp(targetTerritoryId.Value, actualPrompt, out uint? warpId, out string? warpText))
        {
            _logger.LogInformation("Using warp {Id}, {Prompt}", warpId, warpText);
            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
            return true;
        }

        return false;
    }


    private unsafe void DifficultySelectYesNoPostSetup(AddonEvent type, AddonArgs args)
    {
        AtkUnitBase* addonDifficultySelectYesNo = (AtkUnitBase*)args.Addon.Address;
        DifficultySelectYesNoPostSetup(addonDifficultySelectYesNo, false);
    }

    private unsafe void DifficultySelectYesNoPostSetup(AtkUnitBase* addonDifficultySelectYesNo, bool checkAllSteps)
    {
        if (!_questController.IsRunning)
            return;

        var currentQuest = _questController.StartedQuest;
        if (currentQuest == null)
            return;

        var quest = currentQuest.Quest;
        bool autoConfirm;
        if (checkAllSteps)
        {
            var sequence = quest.FindSequence(currentQuest.Sequence);
            autoConfirm = sequence != null && sequence.Steps.Any(step => CheckSinglePlayerDutyYesNo(quest.Id, step));
        }
        else
        {
            var step = quest.FindSequence(currentQuest.Sequence)?.FindStep(currentQuest.Step);
            autoConfirm = step != null && CheckSinglePlayerDutyYesNo(quest.Id, step);
        }

        if (autoConfirm)
        {
            _logger.LogInformation("Confirming difficulty ({Difficulty}) for quest battle", _configuration.SinglePlayerDuties.RetryDifficulty);
            var selectChoice = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 0 },
                new() { Type = ValueType.Int, Int = _configuration.SinglePlayerDuties.RetryDifficulty }
            };
            addonDifficultySelectYesNo->FireCallback(2, selectChoice);
        }
    }

    private ushort? FindTargetTerritoryFromQuestStep(QuestController.QuestProgress currentQuest)
    {
        // this can be triggered either manually (in which case we should increase the step counter), or automatically
        // (in which case it is ~1 frame later, and the step counter has already been increased)
        var sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
        if (sequence == null)
            return null;

        QuestStep? step = sequence.FindStep(currentQuest.Step);
        if (step != null)
            _logger.LogTrace("FindTargetTerritoryFromQuestStep (current): {CurrentTerritory}, {TargetTerritory}",
                step.TerritoryId,
                step.TargetTerritoryId);

        if (step != null && (step.TerritoryId != _clientState.TerritoryType || step.TargetTerritoryId == null) &&
            step.InteractionType == EInteractionType.Gather)
        {
            if (_gatheringPointRegistry.TryGetGatheringPointId(step.ItemsToGather[0].ItemId,
                    (EClassJob?)_clientState.LocalPlayer?.ClassJob.RowId ?? EClassJob.Adventurer,
                    out GatheringPointId? gatheringPointId) &&
                _gatheringPointRegistry.TryGetGatheringPoint(gatheringPointId, out GatheringRoot? root))
            {
                foreach (var gatheringStep in root.Steps)
                {
                    if (gatheringStep.TerritoryId == _clientState.TerritoryType &&
                        gatheringStep.TargetTerritoryId != null)
                    {
                        _logger.LogTrace(
                            "FindTargetTerritoryFromQuestStep (gathering): {CurrentTerritory}, {TargetTerritory}",
                            gatheringStep.TerritoryId,
                            gatheringStep.TargetTerritoryId);
                        return gatheringStep.TargetTerritoryId;
                    }
                }
            }
        }

        if (step == null || step.TargetTerritoryId == null)
        {
            _logger.LogTrace("FindTargetTerritoryFromQuestStep: Checking previous step...");
            step = sequence.FindStep(currentQuest.Step == 255 ? (sequence.Steps.Count - 1) : (currentQuest.Step - 1));

            if (step != null)
                _logger.LogTrace("FindTargetTerritoryFromQuestStep (previous): {CurrentTerritory}, {TargetTerritory}",
                    step.TerritoryId,
                    step.TargetTerritoryId);
        }

        if (step == null || step.TargetTerritoryId == null)
        {
            _logger.LogTrace("FindTargetTerritoryFromQuestStep: Not found");
            return null;
        }

        _logger.LogDebug("Target territory for quest step: {TargetTerritory}", step.TargetTerritoryId);
        return step.TargetTerritoryId;
    }

    private bool TryFindWarp(ushort targetTerritoryId, string actualPrompt, [NotNullWhen(true)] out uint? warpId,
        [NotNullWhen(true)] out string? warpText)
    {
        var warps = _dataManager.GetExcelSheet<Warp>()
            .Where(x => x.RowId > 0 && x.TerritoryType.RowId == targetTerritoryId);
        foreach (var entry in warps)
        {
            string excelName = entry.Name.WithCertainMacroCodeReplacements();
            string excelQuestion = entry.Question.WithCertainMacroCodeReplacements();

            if (!string.IsNullOrEmpty(excelQuestion) && GameFunctions.GameStringEquals(excelQuestion, actualPrompt))
            {
                warpId = entry.RowId;
                warpText = excelQuestion;
                return true;
            }
            else if (!string.IsNullOrEmpty(excelName) && GameFunctions.GameStringEquals(excelName, actualPrompt))
            {
                warpId = entry.RowId;
                warpText = excelName;
                return true;
            }
            else
            {
                _logger.LogDebug("Ignoring prompt '{Prompt}'", excelQuestion);
            }
        }

        warpId = null;
        warpText = null;
        return false;
    }

    private unsafe void PointMenuPostSetup(AddonEvent type, AddonArgs args)
    {
        AtkUnitBase* addonPointMenu = (AtkUnitBase*)args.Addon.Address;
        PointMenuPostSetup(addonPointMenu);
    }

    private unsafe void PointMenuPostSetup(AtkUnitBase* addonPointMenu)
    {
        if (!ShouldHandleUiInteractions)
            return;

        var currentQuest = _questController.StartedQuest;
        if (currentQuest == null)
        {
            _logger.LogInformation("Ignoring point menu, no active quest");
            return;
        }

        var sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
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
        var selectChoice = stackalloc AtkValue[]
        {
            new() { Type = ValueType.Int, Int = 13 },
            new() { Type = ValueType.UInt, UInt = choice }
        };
        addonPointMenu->FireCallback(2, selectChoice);

        currentQuest.IncreasePointMenuCounter();
    }

    private unsafe void HousingSelectBlockPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        _logger.LogInformation("Confirming selected housing ward");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->FireCallbackInt(0);
    }

    private StringOrRegex? ResolveReference(Quest? quest, string? excelSheet, ExcelRef? excelRef, bool isRegExp)
    {
        if (excelRef == null)
            return null;

        if (excelRef.Type == ExcelRef.EType.Key)
            return _excelFunctions.GetDialogueText(quest, excelSheet, excelRef.AsKey(), isRegExp);
        else if (excelRef.Type == ExcelRef.EType.RowId)
            return _excelFunctions.GetDialogueTextByRowId(excelSheet, excelRef.AsRowId(), isRegExp);
        else if (excelRef.Type == ExcelRef.EType.RawString)
            return new StringOrRegex(excelRef.AsRawString());

        return null;
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "HousingSelectBlock", HousingSelectBlockPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "PointMenu", PointMenuPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "DifficultySelectYesNo", DifficultySelectYesNoPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesnoPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectIconString", SelectIconStringPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CutSceneSelectString", CutsceneSelectStringPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", SelectStringPostSetup);
        _questController.AutomationTypeChanged -= HandleCurrentDialogueChoices;
    }

    private sealed record DialogueChoiceInfo(Quest? Quest, DialogueChoice DialogueChoice);
}
