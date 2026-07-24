using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Windows.JournalComponents;

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
    IGameGui gameGui)
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

        if (label != nameof(PriorityWindow))
        {
            using (ImRaii.Disabled(disabled: true))
            {
                var _ = ImGui.MenuItem(_L("Priority Quests"));
            }

            using (ImRaii.PushIndent())
            {
                using (ImRaii.Disabled(quest == null))
                {
                    if (ImGui.MenuItem(_L("Add to Priority Quests")) && quest != null)
                        questController.PriorityManager.Add(quest.Id);
                }
                using (ImRaii.Disabled(prereqs.Count == 0 || quest == null))
                {
                    if (ImGui.MenuItem(_L("Add all to Priority Quests")) && quest != null)
                    {
                        foreach (var qInfo in prereqs)
                            questController.PriorityManager.Add(qInfo.QuestId);
                        questController.PriorityManager.Add(quest.Id);
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
                    commandManager.ProcessCommand($"/questinfo {questInfo.QuestId}");
            }
            using (ImRaii.Disabled(questInfo.QuestId is not QuestId))
            {
                if (ImGui.MenuItem("View on Console Games Wiki"))
                {
                    var query = string.Join('&', new Dictionary<string, string>()
                        {
                            {"search", questInfo.SimplifiedName},
                            {"title", "Special:Search"},
                            {"go", "Go"}
                        }.Select(q => $"{q.Key}={q.Value}"));
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
            if (ImGui.MenuItem(_L("Edit quest path")))
                (bool success, string filename) = QuestRegistry.OpenEditor(questInfo);
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
            ImGui.Checkbox(_L("Hide Quests Without Path"), ref journalUi.Filter.HideNoPaths))
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
}
