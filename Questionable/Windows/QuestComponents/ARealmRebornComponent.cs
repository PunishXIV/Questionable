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

namespace Questionable.Windows.QuestComponents;

internal sealed class ARealmRebornComponent
{
    private static readonly QuestId ATimeForEveryPurpose = new(425);
    private static readonly QuestId TheUltimateWeapon = new(524);
    private static readonly QuestId GoodIntentions = new(363);
    private static readonly ushort[] RequiredPrimalInstances = [20004, 20006, 20005];

    private readonly QuestFunctions _questFunctions;
    private readonly QuestData _questData;
    private readonly TerritoryData _territoryData;
    private readonly UiUtils _uiUtils;
    private readonly Configuration _configuration;

    public ARealmRebornComponent(QuestFunctions questFunctions, QuestData questData, TerritoryData territoryData,
        UiUtils uiUtils, Configuration configuration)
    {
        _questFunctions = questFunctions;
        _questData = questData;
        _territoryData = territoryData;
        _uiUtils = uiUtils;
        _configuration = configuration;
    }

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
        bool complete = UIState.IsInstanceContentCompleted(RequiredPrimalInstances.Last());
        bool hover = _uiUtils.ChecklistItem("Hard Mode Primals", complete,
            _configuration.Advanced.SkipARealmRebornHardModePrimals ? ImGuiColors.DalamudGrey : null);
        if (complete || !hover)
            return;

        using var tooltip = ImRaii.Tooltip();
        if (!tooltip)
            return;

        foreach (var instanceId in RequiredPrimalInstances)
        {
            (Vector4 color, FontAwesomeIcon icon) = UiUtils.GetInstanceStyle(instanceId);
            _uiUtils.ChecklistItem(_territoryData.GetInstanceName(instanceId) ?? "?", color, icon, ImGui.GetStyle().FramePadding.X);
        }
    }

    private void DrawAllianceRaids()
    {
        bool complete = _questFunctions.IsQuestComplete(QuestData.CrystalTowerQuests[^1]);
        bool hover = _uiUtils.ChecklistItem("Crystal Tower Raids", complete,
            _configuration.Advanced.SkipCrystalTowerRaids ? ImGuiColors.DalamudGrey : null);
        if (complete || !hover)
            return;

        using var tooltip = ImRaii.Tooltip();
        if (!tooltip)
            return;

        foreach (var questId in QuestData.CrystalTowerQuests)
        {
            (Vector4 color, FontAwesomeIcon icon, _) = _uiUtils.GetQuestStyle(questId);
            _uiUtils.ChecklistItem(_questData.GetQuestInfo(questId).Name, color, icon, ImGui.GetStyle().FramePadding.X);
        }
    }
}
