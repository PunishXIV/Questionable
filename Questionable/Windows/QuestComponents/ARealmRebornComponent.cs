using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model.Questing;
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
    private readonly Configuration _configuration = configuration;
    private readonly QuestData _questData = questData;

    private readonly QuestFunctions _questFunctions = questFunctions;
    private readonly TerritoryData _territoryData = territoryData;
    private readonly UiUtils _uiUtils = uiUtils;

    public bool ShouldDraw => !_questFunctions.IsQuestAcceptedOrComplete(ATimeForEveryPurpose) &&
                              _questFunctions.IsQuestComplete(TheUltimateWeapon);

    public void Draw()
    {
        if (!_questFunctions.IsQuestAcceptedOrComplete(GoodIntentions))
            DrawPrimals();

        DrawAllianceRaids();
    }

    private void DrawPrimals()
    {
        bool complete = UIState.IsInstanceContentCompleted(RequiredPrimalInstances[^1]);
        bool hover = _uiUtils.ChecklistItem(_L("Hard Mode Primals"), complete,
            _configuration.Advanced.SkipARealmRebornHardModePrimals ? ImGuiColors.DalamudGrey : null);
        if (complete || !hover)
            return;

        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        foreach (ushort instanceId in RequiredPrimalInstances)
        {
            (Vector4 color, FontAwesomeIcon icon) = UiUtils.GetInstanceStyle(instanceId);
            _uiUtils.ChecklistItem(_territoryData.GetInstanceName(instanceId) ?? _L("?"), color, icon, ImGui.GetStyle().FramePadding.X);
        }
    }

    private void DrawAllianceRaids()
    {
        bool complete = _questFunctions.IsQuestComplete(QuestData.CrystalTowerQuests[^1]);
        bool hover = _uiUtils.ChecklistItem(_L("Crystal Tower Raids"), complete,
            _configuration.Advanced.SkipCrystalTowerRaids ? ImGuiColors.DalamudGrey : null);
        if (complete || !hover)
            return;

        using ImRaii.TooltipDisposable tooltip = ImRaii.Tooltip();
        foreach (QuestId questId in QuestData.CrystalTowerQuests)
        {
            (Vector4 color, FontAwesomeIcon icon, string _) = _uiUtils.GetQuestStyle(questId);
            _uiUtils.ChecklistItem(_questData.GetQuestInfo(questId).Name, color, icon, ImGui.GetStyle().FramePadding.X);
        }
    }
}
