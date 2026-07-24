using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model.Questing;
using Questionable.Utils;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows.QuestComponents;

internal sealed class ARealmRebornComponent
(
    QuestFunctions questFunctions,
    QuestData questData,
    TerritoryData territoryData,
    UiUtils uiUtils,
    Configuration configuration)
{
    private static readonly QuestId ATimeForEveryPurpose = new(425);
    private static readonly QuestId TheUltimateWeapon = new(524);
    private static readonly QuestId GoodIntentions = new(363);
    private static readonly ushort[] RequiredPrimalInstances = [20004, 20006, 20005];

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
        bool complete = UIState.IsInstanceContentCompleted(RequiredPrimalInstances[^1]);
        bool hover = uiUtils.ChecklistItem(_L("Hard Mode Primals"), complete,
            configuration.Advanced.SkipARealmRebornHardModePrimals ? ImGuiColors.DalamudGrey : null);
        if (complete || !hover)
            return;

        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        foreach (ushort instanceId in RequiredPrimalInstances)
        {
            (Vector4 color, FontAwesomeIcon icon) = UiUtils.GetInstanceStyle(instanceId);
            uiUtils.ChecklistItem(territoryData.GetInstanceName(instanceId) ?? _L("?"), color, icon, ImGui.GetStyle().FramePadding.X);
        }
    }

    private void DrawAllianceRaids()
    {
        bool complete = questFunctions.IsQuestComplete(QuestData.CrystalTowerQuests[^1]);
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
