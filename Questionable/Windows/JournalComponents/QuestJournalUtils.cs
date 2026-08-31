using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
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
    AutoGen.DraftQuestPathService draftQuestPathService)
{
    public void ShowContextMenu(IQuestInfo questInfo, Quest? quest, string label)
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
        if (label != nameof(PriorityWindow) || inPriority)
        {
            using (ImRaii.Disabled(disabled: true))
            {
                var _ = ImGui.MenuItem(_L("Priority Quests"));
            }

            using (ImRaii.PushIndent())
            {
                if (label != nameof(PriorityWindow))
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
                            foreach (var qInfo in prereqs)
                                questController.PriorityManager.Add(qInfo.QuestId);
                            questController.PriorityManager.Add(quest.Id);
                        }

                        if (ImGui.MenuItem(_L("Add all to Priority Quests as Accept Only")) && quest != null)
                        {
                            foreach (var qInfo in prereqs)
                                questController.PriorityManager.MarkAcceptOnly(qInfo.QuestId);
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
                    questController.Start(label);
                }

                if (ImGui.MenuItem(_L("Set as next quest")))
                    questController.SetNextQuest(quest);
            }

            if (ImGui.MenuItem(_L("Locate quest issuer")))
            {
                MoveToQuestLocation(questInfo, teleport: false);
            }

            bool openInQuestMap = commandManager.Commands.ContainsKey("/questinfo");
            using (ImRaii.Disabled(questInfo.QuestId is not QuestId || !openInQuestMap))
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
                {
                    var query = string.Join('&', new[]
                    {
                        ("search", questInfo.SimplifiedName),
                        ("title", "Special:Search"),
                        ("go", "Go")
                    }.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
                    var uri = new UriBuilder("https", "ffxiv.consolegameswiki.com", 443, "mediawiki/index.php", $"?{query}");
                    Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
                }
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

    internal static void ShowFilterContextMenu(QuestJournalComponent journalUi)
    {
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Filter, ("Filter")))
            ImGui.OpenPopup("##QuestFilters");

        using ImRaii.PopupDisposable popup = ImRaii.Popup("##QuestFilters");
        if (!popup)
            return;

        if (ImGui.Checkbox(_L("Show only Available Quests"), ref journalUi.Filter.AvailableOnly) ||
            ImGui.Checkbox(_L("Show only Blue Quests"), ref journalUi.Filter.BlueOnly) ||
            ImGui.Checkbox(_L("Hide Quests Without Path"), ref journalUi.Filter.HideNoPaths) ||
            ImGui.Checkbox(_L("Hide Completed Quests"), ref journalUi.Filter.HideCompleted) ||
            ImGui.Checkbox(_L("Hide Unobtainable Quests"), ref journalUi.Filter.HideUnobtainable) ||
            ImGui.Checkbox(_L("Hide Repeatable Quests"), ref journalUi.Filter.HideRepeatable))
        {
            journalUi.UpdateFilter();
        }
    }

    public void ShowQuestGroupContextMenu(string note, List<IQuestInfo> quests)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup($"##QuestGroupPopup{note}");

        using ImRaii.PopupDisposable popup = ImRaii.Popup($"##QuestGroupPopup{note}");
        if (!popup)
            return;

        if (ImGui.MenuItem(_L("Add all to Priority Quests")))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Add(quest.QuestId);
        }

        if (ImGui.MenuItem(_L("Remove all from Priority Quests")))
        {
            foreach (IQuestInfo quest in quests)
                questController.PriorityManager.Remove(quest.QuestId);
        }

        // Queues every quest in the group to be accepted before any of them is completed. Unlike
        // "Add all to Priority Quests" (which does each quest start-to-finish one at a time), this lets the
        // user pick up several quests in a group (such as allied societies' dailies) at once,
        // the completion/turn-ins only start once everything queued has been accepted from the priority queue.
        // Does not auto-start questionable, that is up to the user.
        if (ImGui.MenuItem(_L("Accept all quests")))
        {
            foreach (IQuestInfo quest in quests)
            {
                // Only queue quests we can actually accept right now. This skips ones already completed
                // (e.g. today's daily is done) and ones already accepted (the normal rules complete those).
                // FIXME: This probably could allow you to add upcoming quests to get auto-accepted too, or at least better understand why you can't accept them.
                //        For example: if you have a lvl 90 quest and lvl 80 class, it says you are not ready, but you might have a lvl 100 class you could accept with.
                if (questFunctions.IsReadyToAcceptQuest(quest.QuestId))
                    questController.PriorityManager.MarkAcceptOnly(quest.QuestId);
            }
        }

        if (ImGui.MenuItem(_L("Sim first quest")))
            if (quests.Count >= 1)
                questController.SimulateQuest(quests[0], 0, 0);
    }

    public void MoveToQuestLocation(IQuestInfo questInfo, bool teleport = true)
    {
        var location = ((QuestInfo)questInfo).IssuerLocation;
        Svc.Log.Debug(location.ToString() ?? "SheetLevel()");
        var mapLink = new MapLinkPayload(
            location.Territory.RowId,
            location.Map.RowId,
            location.Game.X,
            location.Game.Z
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
            if (aetheryteData.NearestAetheryteTo(location.Territory.RowId, location.Position) is { } aetheryte)
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
        if (icon is FontAwesomeIcon.Times)
            iconOverride = questInfo.InvalidIcon;
        if (icon is FontAwesomeIcon.Check)
            iconOverride = questInfo.CompleteIcon;
        if (questInfo.IsRepeatable && iconOverride % 10 < 2)
            iconOverride = iconOverride + (2 - iconOverride % 10);
        if (icon is FontAwesomeIcon.QuestionCircle)
            iconOverride = QuestionIcon;
        return iconOverride;
    }
}
