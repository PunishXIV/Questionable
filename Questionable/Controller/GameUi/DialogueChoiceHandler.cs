using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Utils;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Handles the in-game "SelectString", "CutSceneSelectString" and "SelectIconString" addons —
///     picking the list entry that matches the current quest's dialogue choices.
/// </summary>
internal sealed class DialogueChoiceHandler : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGuiAdapter _gameGui;
    private readonly QuestController _questController;
    private readonly QuestFunctions _questFunctions;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestData _questData;
    private readonly AetheryteFunctions _aetheryteFunctions;
    private readonly TerritoryData _territoryData;
    private readonly IClientState _clientState;
    private readonly ITargetManager _targetManager;
    private readonly ExcelFunctions _excelFunctions;
    private readonly DialogueReferenceResolver _dialogueReferenceResolver;
    private readonly TravelDestinationResolver _travelDestinationResolver;
    private readonly Configuration _configuration;
    private readonly IChatGui _chatGui;
    private readonly IToastGui _toastGui;
    private readonly ILogger<DialogueChoiceHandler> _logger;

    public DialogueChoiceHandler(
        IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGui,
        QuestController questController,
        QuestFunctions questFunctions,
        QuestRegistry questRegistry,
        QuestData questData,
        AetheryteFunctions aetheryteFunctions,
        TerritoryData territoryData,
        IClientState clientState,
        ITargetManager targetManager,
        ExcelFunctions excelFunctions,
        Configuration configuration,
        IChatGui chatGui,
        IToastGui toastGui,
        DialogueReferenceResolver dialogueReferenceResolver,
        TravelDestinationResolver travelDestinationResolver,
        ILogger<DialogueChoiceHandler> logger)
    {
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _questController = questController;
        _questFunctions = questFunctions;
        _questRegistry = questRegistry;
        _questData = questData;
        _aetheryteFunctions = aetheryteFunctions;
        _territoryData = territoryData;
        _clientState = clientState;
        _targetManager = targetManager;
        _excelFunctions = excelFunctions;
        _configuration = configuration;
        _chatGui = chatGui;
        _toastGui = toastGui;
        _dialogueReferenceResolver = dialogueReferenceResolver;
        _travelDestinationResolver = travelDestinationResolver;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", SelectStringPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "CutSceneSelectString", CutsceneSelectStringPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectIconString", SelectIconStringPostSetup);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectIconString", SelectIconStringPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CutSceneSelectString", CutsceneSelectStringPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", SelectStringPostSetup);
    }

    private bool ShouldHandleUiInteractions =>
        _questController.IsRunning ||
        _territoryData.IsQuestBattleInstance(_clientState.TerritoryType);

    private unsafe void SelectStringPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        HandleSelectString((AddonSelectString*)args.Addon.Address, checkAllSteps: false);
    }

    private unsafe void CutsceneSelectStringPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        HandleCutsceneSelectString((AddonCutSceneSelectString*)args.Addon.Address, checkAllSteps: false);
    }

    private unsafe void SelectIconStringPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldHandleUiInteractions)
            return;

        HandleSelectIconString((AddonSelectIconString*)args.Addon.Address, checkAllSteps: false);
    }

    /// <summary>
    ///     Handles any SelectString / CutSceneSelectString / SelectIconString addon already open when
    ///     automation starts. Bypasses the <see cref="ShouldHandleUiInteractions"/> gate, matching the
    ///     legacy "initial check" behaviour.
    /// </summary>
    public unsafe void CheckExisting()
    {
        if (_gameGui.TryGetAddonByName("SelectString", out AddonSelectString* addonSelectString))
        {
            _logger.LogInformation("SelectString window is open");
            HandleSelectString(addonSelectString, checkAllSteps: true);
        }

        if (_gameGui.TryGetAddonByName("CutSceneSelectString", out AddonCutSceneSelectString* addonCutSceneSelectString))
        {
            _logger.LogInformation("CutSceneSelectString window is open");
            HandleCutsceneSelectString(addonCutSceneSelectString, checkAllSteps: true);
        }

        if (_gameGui.TryGetAddonByName("SelectIconString", out AddonSelectIconString* addonSelectIconString))
        {
            _logger.LogInformation("SelectIconString window is open");
            HandleSelectIconString(addonSelectIconString, checkAllSteps: true);
        }
    }

    private unsafe void HandleSelectString(AddonSelectString* addonSelectString, bool checkAllSteps)
    {
        string? actualPrompt = AtkValueAdapter.ReadString(addonSelectString->AtkUnitBase.AtkValues[2]);
        if (actualPrompt == null)
            return;

        AddonMaster.SelectString master = new(addonSelectString);
        List<string?> answers = [.. master.Entries.Select(x => (string?)x.Text)];

        int? answer = HandleListChoice(actualPrompt, answers, checkAllSteps) ?? HandleInstanceListChoice(actualPrompt);
        if (answer != null)
        {
            _logger.LogInformation("Using choice {Choice} for list prompt '{Prompt}'", answer, actualPrompt);
            if (!_configuration.General.DontSkipCutscenes)
                addonSelectString->AtkUnitBase.FireCallbackInt(answer.Value);
        }
    }

    private unsafe void HandleCutsceneSelectString(AddonCutSceneSelectString* addonCutSceneSelectString,
        bool checkAllSteps)
    {
        string? actualPrompt = AtkValueAdapter.ReadString(addonCutSceneSelectString->AtkUnitBase.AtkValues[2]);
        if (actualPrompt == null)
            return;

        List<string?> answers = [];
        for (int i = 5; i < addonCutSceneSelectString->AtkUnitBase.AtkValuesCount; ++i)
        {
            answers.Add(AtkValueAdapter.ReadString(addonCutSceneSelectString->AtkUnitBase.AtkValues[i]));
        }

        int? answer = HandleListChoice(actualPrompt, answers, checkAllSteps);
        if (answer != null && !_configuration.General.DontSkipCutscenes)
            addonCutSceneSelectString->AtkUnitBase.FireCallbackInt(answer.Value);
    }

    private unsafe void HandleSelectIconString(AddonSelectIconString* addonSelectIconString, bool checkAllSteps)
    {
        string? actualPrompt = AtkValueAdapter.ReadString(addonSelectIconString->AtkUnitBase.AtkValues[3]);
        if (string.IsNullOrEmpty(actualPrompt))
            actualPrompt = null;

        List<string?> answers = GetChoices(addonSelectIconString);
        int? answer = HandleListChoice(actualPrompt, answers, checkAllSteps);
        if (answer != null)
        {
            _logger.LogInformation("Using choice {Choice} for list prompt '{Prompt}'", answer, actualPrompt);
            if (!_configuration.General.DontSkipCutscenes)
                addonSelectIconString->AtkUnitBase.FireCallbackInt(answer.Value);
            return;
        }

        // this is 'Daily Quests' for tribal quests, but not set for normal selections
        string? title = AtkValueAdapter.ReadString(addonSelectIconString->AtkValues[0]);

        QuestController.QuestProgress? currentQuest = _questController.StartedQuest;
        if (currentQuest != null && (actualPrompt == null || title != null))
        {
            _logger.LogInformation("Checking if current quest {Name} is on the list", currentQuest.Quest.Info.Name);
            if (CheckQuestSelection(addonSelectIconString, currentQuest.Quest, answers))
                return;

            QuestSequence? sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
            QuestStep? step = sequence?.FindStep(currentQuest.Step);
            if (step is { InteractionType: EInteractionType.AcceptQuest, PickUpQuestId: not null } &&
                _questRegistry.TryGetQuest(step.PickUpQuestId, out Quest? pickupQuest))
            {
                _logger.LogInformation("Checking if current picked-up {Name} is on the list", pickupQuest.Info.Name);
                if (CheckQuestSelection(addonSelectIconString, pickupQuest, answers))
                    return;
            }
        }

        QuestController.QuestProgress? nextQuest = _questController.NextQuest;
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
        AddonMaster.SelectIconString master = new(addonSelectIconString);
        return [.. master.Entries.Select(x => (string?)x.Text)];
    }

    private int? HandleListChoice(string? actualPrompt, List<string?> answers, bool checkAllSteps)
    {
        List<DialogueChoiceInfo> dialogueChoices = [];

        QuestController.QuestProgress? currentQuest = _questController.SimulatedQuest ??
                                                      _questController.GatheringQuest ??
                                                      _questController.StartedQuest;
        if (currentQuest != null)
        {
            Quest quest = currentQuest.Quest;
            bool isTaxiStandUnlock = false;
            List<EAetheryteLocation> freeOrFavoredAetheryteRegistrations = [];
            if (checkAllSteps)
            {
                QuestSequence? sequence = quest.FindSequence(currentQuest.Sequence);
                IEnumerable<DialogueChoice>? choices = sequence?.Steps.SelectMany(x => x.DialogueChoices);
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

                step ??= quest.FindSequence(currentQuest.Sequence)?.FindStep(currentQuest.Step);

                if (step == null)
                    _logger.LogDebug("Ignoring current quest dialogue choices, no active step");
                else
                {
                    dialogueChoices.AddRange(step.DialogueChoices.Select(x => new DialogueChoiceInfo(quest, x)));
                    if (step.PurchaseMenu != null)
                    {
                        dialogueChoices.Add(new(quest, new()
                        {
                            Type = EDialogChoiceType.List,
                            ExcelSheet = step.PurchaseMenu.ExcelSheet,
                            Prompt = null,
                            Answer = step.PurchaseMenu.Key
                        }));
                    }

                    if (step is { InteractionType: EInteractionType.RegisterFreeOrFavoredAetheryte, Aetheryte: { } aetheryte })
                        freeOrFavoredAetheryteRegistrations = [aetheryte];

                    isTaxiStandUnlock = step.InteractionType == EInteractionType.UnlockTaxiStand;
                }
            }

            if (isTaxiStandUnlock)
            {
                _logger.LogInformation("Adding chocobo taxi stand unlock dialogue choices");
                dialogueChoices.Add(new(quest, new()
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
                dialogueChoices.Add(new(quest, new()
                {
                    Type = EDialogChoiceType.List,
                    ExcelSheet = "transport/Aetheryte",
                    Prompt = ExcelRef.FromKey("TEXT_AETHERYTE_MAINMENU_TITLE"),
                    PromptIsRegularExpression = true,
                    Answer = ExcelRef.FromKey("TEXT_AETHERYTE_REGISTER_TOKEN_FAVORITE"),
                    AnswerIsRegularExpression = true
                }));
            }
            else if (freeOrFavoredAetheryteRegistrations.Any(x =>
                _aetheryteFunctions.CanRegisterFreeOrFavoriteAetheryte(x) ==
                AetheryteRegistrationResult.FavoredDestinationAvailable))
            {
                _logger.LogInformation("Adding favored aetheryte unlock dialogue choice");
                dialogueChoices.Add(new(quest, new()
                {
                    Type = EDialogChoiceType.List,
                    ExcelSheet = "transport/Aetheryte",
                    Prompt = ExcelRef.FromKey("TEXT_AETHERYTE_MAINMENU_TITLE"),
                    PromptIsRegularExpression = true,
                    Answer = ExcelRef.FromKey("TEXT_AETHERYTE_REGISTER_FAVORITE"),
                    AnswerIsRegularExpression = true
                }));
            }

            // add all travel dialogue choices
            ushort? targetTerritoryId = _travelDestinationResolver.FindTargetTerritoryFromQuestStep(currentQuest);
            if (targetTerritoryId != null)
            {
                foreach (string? answer in answers)
                {
                    if (answer == null)
                        continue;

                    if (_travelDestinationResolver.TryFindWarp(targetTerritoryId.Value, answer, out uint? warpId, out string? warpText))
                    {
                        _logger.LogInformation("Adding warp {Id}, {Prompt}", warpId, warpText);
                        dialogueChoices.Add(new(quest, new()
                        {
                            Type = EDialogChoiceType.List,
                            ExcelSheet = null,
                            Prompt = null,
                            Answer = ExcelRef.FromSheetValue(warpText)
                        }));
                    }
                }
            }
        }
        else
            _logger.LogDebug("Ignoring current quest dialogue choices, no active quest");

        // add all quests that start with the targeted npc
        IGameObject? target = _targetManager.Target;
        if (target != null)
        {
            foreach (IQuestInfo questInfo in _questData.GetAllByIssuerDataId(GameFunctions.GetBaseID(target)).Where(x => x.QuestId is QuestId))
            {
                if (_questFunctions.IsReadyToAcceptQuest(questInfo.QuestId) &&
                    _questRegistry.TryGetQuest(questInfo.QuestId, out Quest? knownQuest))
                {
                    List<DialogueChoice>? questChoices = knownQuest.FindSequence(0)?.Steps
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

        foreach ((Quest? quest, DialogueChoice dialogueChoice) in dialogueChoices)
        {
            if (dialogueChoice.Type != EDialogChoiceType.List)
                continue;

            if (dialogueChoice.SpecialCondition == "NoDutyActions")
            {
                try
                {
                    unsafe
                    {
                        if (RaptureHotbarModule.Instance()->DutyActionsPresent)
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

            if (dialogueChoice.DataId != null && dialogueChoice.DataId != GameFunctions.GetBaseID(_targetManager.Target))
            {
                _logger.LogDebug(
                    "Skipping entry in DialogueChoice expecting target dataId {ExpectedDataId}, actual target is {ActualTargetId}",
                    dialogueChoice.DataId, GameFunctions.GetBaseID(_targetManager.Target));
                continue;
            }

            StringOrRegex? excelPrompt = _dialogueReferenceResolver.Resolve(quest, dialogueChoice.ExcelSheet, dialogueChoice.Prompt,
                dialogueChoice.PromptIsRegularExpression);
            StringOrRegex? excelAnswer = _dialogueReferenceResolver.Resolve(quest, dialogueChoice.ExcelSheet, dialogueChoice.Answer,
                dialogueChoice.AnswerIsRegularExpression);

            if (actualPrompt == null && excelPrompt != null)
            {
                _logger.LogInformation("Unexpected excelPrompt: {ExcelPrompt}", excelPrompt);
                continue;
            }

            if (actualPrompt != null &&
                (excelPrompt == null || !DialogueReferenceResolver.IsMatch(actualPrompt, excelPrompt)))
            {
                _logger.LogInformation("Unexpected excelPrompt: {ExcelPrompt}, actualPrompt: {ActualPrompt}",
                    excelPrompt, actualPrompt);
                continue;
            }

            for (int i = 0; i < answers.Count; ++i)
            {
                _logger.LogTrace("Checking if {ActualAnswer} == {ExpectedAnswer}",
                    answers[i], excelAnswer);
                if (DialogueReferenceResolver.IsMatch(answers[i], excelAnswer))
                {
                    string notif = $"Question: '{actualPrompt}', {(_configuration.General.DontSkipCutscenes ? "scripted:" : "answering")} '{answers[i]}'";
                    if (_configuration.General.DontSkipCutscenes)
                        _toastGui.ShowQuest(notif);
                    _chatGui.Print(notif, CommandHandler.MessageTag, CommandHandler.TagColor);
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

    private sealed record DialogueChoiceInfo(Quest? Quest, DialogueChoice DialogueChoice);
}
