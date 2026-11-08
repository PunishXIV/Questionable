using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.AutoGen.Plugin;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.JournalComponents;

[RegisterSingleton]
internal sealed class QuestJournalUtils
(
    QuestController questController,
    QuestFunctions questFunctions,
    ICommandManager commandManager,
    Configuration configuration,
    IDalamudPluginInterface pluginInterface,
    AetheryteData aetheryteData,
    AetheryteFunctions aetheryteFunctions,
    MovementController movementController,
    IGameGui gameGui,
    PathEditorWindow pathEditorWindow,
    RedoUtil redoUtil,
    DraftQuestPathService draftQuestPathService)
{
    public void ShowContextMenu(IQuestInfo questInfo, Quest? quest, string source)
    {
        List<IQuestInfo> prereqs = [];
        if (questFunctions.prereqCache.TryGetValue(questInfo.QuestId.Value, out HashSet<IQuestInfo>? value))
            prereqs = value.Where(q => !questFunctions.IsQuestComplete(q.QuestId)).ToList();
        prereqs.Sort(Comparer<IQuestInfo>.Create((x, y) =>
        {
            var xCount = questFunctions.prereqCache.TryGetValue(x.QuestId.Value, out var xList) ? xList.Count : 0;
            var yCount = questFunctions.prereqCache.TryGetValue(y.QuestId.Value, out var yList) ? yList.Count : 0;
            return xCount.CompareTo(yCount);
        }));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestPopup{questInfo.QuestId}");

        using ImRaii.PopupDisposable popup = ImRaii.Popup($"##QuestPopup{questInfo.QuestId}");
        if (!popup)
            return;

        bool inPriority = quest != null && questController.PriorityManager.Contains(quest);
        if (source != nameof(PriorityWindow) || inPriority)
        {
            using (ImRaii.Disabled(disabled: true))
            {
                var _ = ImGui.MenuItem(_L("Priority Quests"));
            }

            using (ImRaii.PushIndent())
            {
                if (source != nameof(PriorityWindow))
                {
                    using (ImRaii.Disabled(quest == null))
                    {
                        if (ImGui.MenuItem(_L("Add to Priority Quests")) && quest != null)
                            questController.PriorityManager.Add(quest.Id);

                        if (ImGui.MenuItem(_L("Add to Priority Quests as Accept Only")) && quest != null)
                            questController.PriorityManager.MarkAcceptOnly(quest.Id);
                    }
                    using (ImRaii.Disabled(prereqs.Count == 0 || quest == null))
                    {
                        if (ImGui.MenuItem(_L("Add all to Priority Quests")) && quest != null)
                        {
                            foreach (var prereq in prereqs)
                                if (!questFunctions.IsQuestUnobtainable(prereq.QuestId) && !questFunctions.IsQuestComplete(prereq.QuestId))
                                    questController.PriorityManager.Add(prereq.QuestId);
                            questController.PriorityManager.Add(quest.Id);
                        }

                        if (ImGui.MenuItem(_L("Add all to Priority Quests as Accept Only")) && quest != null)
                        {
                            foreach (var prereq in prereqs)
                                if (!questFunctions.IsQuestUnobtainable(prereq.QuestId) && !questFunctions.IsQuestComplete(prereq.QuestId))
                                    questController.PriorityManager.MarkAcceptOnly(prereq.QuestId);
                            questController.PriorityManager.MarkAcceptOnly(quest.Id);
                        }
                    }
                }
                else if (inPriority && quest != null)
                {
                    if (questController.PriorityManager.IsAcceptOnly(quest.Id))
                    {
                        if (ImGui.MenuItem(_L("Change to normal priority quest")))
                            questController.PriorityManager.ClearAcceptOnly(quest.Id);
                    }
                    else
                    {
                        if (ImGui.MenuItem(_L("Change to accept only")))
                            questController.PriorityManager.MarkAcceptOnly(quest.Id);
                    }
                }
            }
        }

        using (ImRaii.Disabled(disabled: true))
        {
            var _ = ImGui.MenuItem(_L("Quest"));
        }

        using (ImRaii.PushIndent())
        {
            using (ImRaii.Disabled(!questFunctions.IsReadyToAcceptQuest(questInfo.QuestId)))
            {
                if (ImGui.MenuItem(_L("Start as next quest")))
                {
                    questController.SetNextQuest(quest);
                    questController.Start(source);
                }

                if (ImGui.MenuItem(_L("Set as next quest")))
                    questController.SetNextQuest(quest);
            }

            if (ImGui.MenuItem(_L("Locate quest issuer")))
            {
                MoveToIssuer(questInfo, teleport: false);
            }

            bool hasQuestMap = commandManager.Commands.ContainsKey("/questinfo");
            using (ImRaii.Disabled(questInfo.QuestId is not QuestId || !hasQuestMap))
            {
                if (ImGui.MenuItem(_L("View in Quest Map")))
                    if (commandManager.Commands.ContainsKey("/questgraph"))
                        commandManager.ProcessCommand($"/questgraph {questInfo.QuestId}");
                    else
                        commandManager.ProcessCommand($"/questinfo {questInfo.QuestId}");
            }
            using (ImRaii.Disabled(questInfo.QuestId is not QuestId))
            {
                if (ImGui.MenuItem("View on Console Games Wiki"))
                    MoreInfoUtils.SearchConsoleGamesWiki(questInfo.SimplifiedName);
            }
            if (configuration.Advanced.Debug)
            {
                RedoIndex redoIndex = redoUtil.GetChapter(questInfo.QuestId.Value);
                bool redoActive = redoUtil.IsActive();
                using (ImRaii.Disabled(redoActive || redoIndex.Chapter.RowId == 0 || redoIndex.Chapter.ChapterName.ToString().Length == 0))
                {
                    if (ImGui.MenuItem(_L("Start NG+ here")))
                    {
                        if (redoActive) // safeguard
                            redoUtil.SendCommand(redoChapter: RedoChapter.Off);
                        else
                            redoUtil.SendCommand(questRedoChapter: redoIndex.Chapter);
                    }
                }
                if (redoActive && ImGui.MenuItem(_L("Stop NG+")))
                    redoUtil.SendCommand(redoChapter: RedoChapter.Off);
            }
        }

        using (ImRaii.Disabled(disabled: true))
        {
            var _ = ImGui.MenuItem(_L("Stop"));
        }

        using (ImRaii.PushIndent())
        {
            if (ImGui.MenuItem(_L("Add to Stop condition (on complete)")))
            {
                configuration.Stop.QuestsToStopAfter.Add(questInfo.QuestId);
                pluginInterface.SavePluginConfig(configuration);
            }

            if (ImGui.MenuItem(_L("Add to Stop condition (on accept)")))
            {
                configuration.Stop.QuestsToStopWhenAccepted.Add(questInfo.QuestId);
                pluginInterface.SavePluginConfig(configuration);
            }
        }

        using (ImRaii.Disabled(disabled: true))
        {
            var _ = ImGui.MenuItem(_L("Path data"));
        }

        using (ImRaii.PushIndent())
        {
            if (ImGui.MenuItem(_L("Open in Path Editor")))
                pathEditorWindow.Open(questInfo.QuestId);

            if (ImGui.MenuItem(_L("Edit quest path")))
                (bool success, string filename) = QuestRegistry.OpenEditor(questInfo);

            // Only offered while the quest has no path at all; once the draft is written and the registry
            // reloads, the quest is known and the entry disappears on its own.
            if (draftQuestPathService.CanGenerateDrafts &&
                ImGui.MenuItem(_L("Generate draft path")))
            {
                draftQuestPathService.GenerateDraft(questInfo);
            }

            if (ImGui.MenuItem(_L("Sim quest")))
                questController.SimulateQuest(questInfo, 0, 0);
        }
    }

    internal static void ShowFilterContextMenu(QuestJournalComponent journal)
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Filter, ("Filter")))
            ImGui.OpenPopup("##QuestFilters");

        using ImRaii.PopupDisposable popup = ImRaii.Popup("##QuestFilters");
        if (!popup)
            return;

        if (ImGui.Checkbox(_L("Show only Available Quests"), ref journal.Filter.AvailableOnly) ||
            ImGui.Checkbox(_L("Show only Blue Quests"), ref journal.Filter.BlueOnly) ||
            ImGui.Checkbox(_L("Hide Quests Without Path"), ref journal.Filter.HideNoPaths) ||
            ImGui.Checkbox(_L("Hide Completed Quests"), ref journal.Filter.HideCompleted) ||
            ImGui.Checkbox(_L("Hide Unobtainable Quests"), ref journal.Filter.HideUnobtainable) ||
            ImGui.Checkbox(_L("Hide Repeatable Quests"), ref journal.Filter.HideRepeatable))
        {
            journal.UpdateFilter();
        }
    }

    public void ShowGroupContextMenu(string id, List<IQuestInfo> quests)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestGroupPopup{id}");

        using ImRaii.PopupDisposable popup = ImRaii.Popup($"##QuestGroupPopup{id}");
        if (!popup)
            return;

        if (ImGui.MenuItem(_L("Add all to Priority Quests")))
        {
            foreach (IQuestInfo quest in quests)
                if (!questFunctions.IsQuestUnobtainable(quest.QuestId) && !questFunctions.IsQuestComplete(quest.QuestId))
                    questController.PriorityManager.Add(quest.QuestId);
        }

        if (ImGui.MenuItem(_L("Remove all from Priority Quests")))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Remove(quest.QuestId);
        }

        // Queue the group's accept steps before any completions, unlike the normal priority flow.
        if (ImGui.MenuItem(_L("Accept all quests")))
        {
            foreach (IQuestInfo quest in quests)
            {
                // Only queue quests we can actually accept. This skips ones already completed (e.g. today's daily
                // is done) and ones already accepted (the normal rules complete those). The current class doesn't
                // need the level, as long as another class does: the accept step switches to it.
                if (questFunctions.IsReadyToAcceptQuest(quest.QuestId, ignoreLevel: true) &&
                    questFunctions.HasClassJobAtQuestLevel(quest.QuestId))
                    questController.PriorityManager.MarkAcceptOnly(quest.QuestId);
            }
        }

        if (ImGui.MenuItem(_L("Sim first quest")))
            if (quests.Count >= 1)
                questController.SimulateQuest(quests[0], 0, 0);
    }

    public void MoveToIssuer(IQuestInfo questInfo, bool teleport = true)
    {
        var location = ((QuestInfo)questInfo).IssuerLocation;
        Svc.Log.Debug(location.ToString() ?? "SheetLevel()");
        var mapLink = new MapLinkPayload(
            location.Territory.RowId,
            location.Map.RowId,
            location.MapCoords.X,
            location.MapCoords.Z
        );
        var _ = gameGui.OpenMapWithMapLink(mapLink);
        if (!teleport)
            return;
        if (location.Territory.RowId.Equals(Svc.ClientState.TerritoryType))
            movementController.NavigateTo(EMovementType.None, questInfo.IssuerDataId, location.Position, new()
            {
                Fly = GameFunctions.IsFlyingUnlocked(location.Territory.RowId),
                Sprint = true,
                StopDistance = 20f,
                VerticalStopDistance = 5f,
            });
        else
            if (aetheryteData.NearestTo(location.Territory.RowId, location.Position) is { } aetheryte)
                aetheryteFunctions.TeleportAetheryte(aetheryte);
    }

    public static uint? GetIconOverride(QuestInfo questInfo, FontAwesomeIcon? icon = null, Vector4? color = null)
    {
        const uint QuestionIcon = 71226;
        const uint BlueRepeatable = 71342;
        uint? iconOverride = null;
        if (color != null)
        {
            if (color == QstTheme.Info)
                iconOverride = BlueRepeatable;
            else if (icon is FontAwesomeIcon.Running)
                iconOverride = questInfo.AvailableIcon + 1;
        }
        else if (icon is FontAwesomeIcon.Running)
            iconOverride = questInfo.AvailableIcon;
        if (icon is FontAwesomeIcon.PersonWalkingArrowRight)
            iconOverride = questInfo.ActiveIcon;
        if (icon is FontAwesomeIcon.Lock)
            iconOverride = questInfo.InvalidIcon;
        if (icon is FontAwesomeIcon.Check)
            iconOverride = questInfo.CompleteIcon;
        if (questInfo.IsRepeatable && iconOverride % 10 < 2)
            iconOverride += (2 - iconOverride % 10);
        if (icon is FontAwesomeIcon.QuestionCircle)
            iconOverride = QuestionIcon;
        return iconOverride;
    }
}
