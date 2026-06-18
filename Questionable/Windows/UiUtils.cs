using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Windows;

internal sealed class UiUtils(QuestFunctions questFunctions, IDalamudPluginInterface pluginInterface)
{
    public (Vector4 Color, FontAwesomeIcon Icon, string Status) GetQuestStyle(ElementId elementId)
    {
        HashSet<IQuestInfo>? prereqValue = null;
        if (questFunctions.IsQuestAccepted(elementId))
            return (ImGuiColors.DalamudYellow, FontAwesomeIcon.PersonWalkingArrowRight, _L("Active"));
        else if (elementId is QuestId questId && questFunctions.IsDailyAlliedSocietyQuestAndAvailableToday(questId))
        {
            if (!questFunctions.IsReadyToAcceptQuest(questId))
                return (ImGuiColors.ParsedGreen, FontAwesomeIcon.Check, _L("Complete"));
            else if (questFunctions.IsQuestComplete(questId))
                return (ImGuiColors.ParsedBlue, FontAwesomeIcon.Running, _L("Available"));
            else
                return (ImGuiColors.DalamudYellow, FontAwesomeIcon.Running, _L("Available"));
        }
        else if (questFunctions.IsQuestAcceptedOrComplete(elementId))
            return (ImGuiColors.ParsedGreen, FontAwesomeIcon.Check, _L("Complete"));
        else if (questFunctions.IsQuestUnobtainable(elementId))
            return (ImGuiColors.DalamudGrey, FontAwesomeIcon.Minus, _L("Unobtainable"));
        else if (questFunctions.IsQuestLocked(elementId) is (bool isLocked, var _) && isLocked &&
                questFunctions.prereqCache.TryGetValue(elementId.Value, out prereqValue) &&
                prereqValue.Any(q => questFunctions.IsQuestLocked(q.QuestId) is (bool qIsLocked, string[] reasons) && qIsLocked && !reasons.Contains("Unobtainable")))
            return (ImGuiColors.DalamudRed, FontAwesomeIcon.Times, _L("Locked"));
        else if (prereqValue == null)
            return (ImGuiColors.TankBlue, FontAwesomeIcon.QuestionCircle, _L("Available(?)"));
        else
            return (ImGuiColors.DalamudYellow, FontAwesomeIcon.Running, _L("Available"));
    }

    public static (Vector4 color, FontAwesomeIcon icon) GetInstanceStyle(ushort instanceId)
    {
        if (UIState.IsInstanceContentCompleted(instanceId))
            return (ImGuiColors.ParsedGreen, FontAwesomeIcon.Check);
        else if (UIState.IsInstanceContentUnlocked(instanceId))
            return (ImGuiColors.DalamudYellow, FontAwesomeIcon.Running);
        else
            return (ImGuiColors.DalamudRed, FontAwesomeIcon.Times);
    }

    public bool ChecklistItem(string text, Vector4 color, FontAwesomeIcon icon, float extraPadding = 0)
    {
        if (extraPadding > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + extraPadding);

        using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            ImGui.TextColored(color, icon.ToIconString());
        }

        bool hover = ImGui.IsItemHovered();

        ImGui.SameLine();
        if (extraPadding > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + extraPadding);
        ImGui.TextUnformatted(text);
        hover |= ImGui.IsItemHovered();
        return hover;
    }

    public bool ChecklistItem(string text, bool complete, Vector4? colorOverride = null)
    {
        return ChecklistItem(text,
            colorOverride ?? (complete ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed),
            complete ? FontAwesomeIcon.Check : FontAwesomeIcon.Times);
    }
}
