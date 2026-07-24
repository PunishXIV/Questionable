using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Model.Questing;
namespace Questionable.Windows.QuestComponents;

internal sealed class ARealmRebornComponent
(
    GameFunctions gameFunctions,
    QuestFunctions questFunctions,
    QuestData questData,
    QuestController questController,
    QuestRegistry questRegistry,
    TerritoryData territoryData,
    UiUtils uiUtils,
    Configuration configuration)
{
    private static readonly QuestId ATimeForEveryPurpose = new(425);
    private static readonly QuestId TheUltimateWeapon = new(524);
    private static readonly QuestId GoodIntentions = new(363);
    private static readonly Dictionary<ushort, ushort> RequiredPrimalInstances =
        new() { { 20004, 59 }, { 20006, 60 }, { 20005, 61 } };

    public bool ShouldDraw => !questFunctions.IsQuestAcceptedOrComplete(ATimeForEveryPurpose) &&
                              questFunctions.IsQuestComplete(TheUltimateWeapon);

    public void Draw()
    {
        if (!questFunctions.IsQuestAcceptedOrComplete(GoodIntentions))
            DrawPrimals();

        DrawAllianceRaids();
    }

    private void DrawPrimals()
    {
        bool complete = UIState.IsInstanceContentCompleted(RequiredPrimalInstances.Keys.Last());
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ExclamationCircle))
        {
            foreach (ushort qId in new ushort[] { 1048, 1157, 1158 })
                if (questRegistry.TryGetQuest(new QuestId(qId), out var quest) &&
                        !questFunctions.IsQuestComplete(quest.Id))
                    questController.PriorityManager.Add(quest);
            foreach (ushort instanceId in RequiredPrimalInstances.Keys)
            {
                if (!UIState.IsInstanceContentCompleted(instanceId) &&
                    UIState.IsInstanceContentUnlocked(instanceId))
                {
                    gameFunctions.OpenDutyFinder(contentFinderConditionId: RequiredPrimalInstances[instanceId]);
                    break;
                }
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Add to priority quests"));
        ImGui.SameLine();
        bool hover = uiUtils.ChecklistItem(_L("Hard Mode Primals"), complete,
            configuration.Advanced.SkipARealmRebornHardModePrimals ? ImGuiColors.DalamudGrey : null);
        if (complete || !hover)
            return;

        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        foreach (ushort instanceId in RequiredPrimalInstances.Keys)
        {
            (Vector4 color, FontAwesomeIcon icon) = UiUtils.GetInstanceStyle(instanceId);
            uiUtils.ChecklistItem(territoryData.GetInstanceName(instanceId) ?? _L("?"), color, icon, ImGui.GetStyle().FramePadding.X);
        }
    }

    private void DrawAllianceRaids()
    {
        bool complete = questFunctions.IsQuestComplete(QuestData.CrystalTowerQuests[^1]);
        if (ImGuiComponentsLocal.IconButton(FontAwesomeIcon.ExclamationCircle))
        {
            foreach (QuestId qId in QuestData.CrystalTowerQuests)
                if (questRegistry.TryGetQuest(qId, out var quest) &&
                        !questFunctions.IsQuestComplete(qId))
                    questController.PriorityManager.Add(quest);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Add to priority quests"));
        ImGui.SameLine();
        bool hover = uiUtils.ChecklistItem(_L("Crystal Tower Raids"), complete,
            configuration.Advanced.SkipCrystalTowerRaids ? ImGuiColors.DalamudGrey : null);
        if (complete || !hover)
            return;

        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        foreach (QuestId questId in QuestData.CrystalTowerQuests)
        {
            (Vector4 color, FontAwesomeIcon icon, string _) = uiUtils.GetQuestStyle(questId);
            uiUtils.ChecklistItem(questData.GetQuestInfo(questId).Name, color, icon, ImGui.GetStyle().FramePadding.X);
        }
    }
}
