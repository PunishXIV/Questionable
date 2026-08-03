using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows;

internal sealed class UiUtils(QuestFunctions questFunctions, IDalamudPluginInterface pluginInterface, QuestData questData)
{
    public (Vector4 Color, FontAwesomeIcon Icon, string Status) GetQuestStyle(ElementId elementId)
    {
        string lockedReason = string.Empty;
        questFunctions.prereqCache.TryGetValue(elementId.Value, out var prereqValue);
        if (questFunctions.IsQuestLocked(elementId) is (bool isLocked, string[] reasons) && isLocked)
            lockedReason = string.Join("\n  ", reasons);
        else if (questData.TryGetQuestInfo(elementId, out var qInfo) && qInfo is IQuestInfo questInfo &&
                questInfo.PreviousQuestJoin is EQuestJoin.All &&
                prereqValue != null && prereqValue.Any(q => questFunctions.IsQuestLocked(q.QuestId) is (bool qIsLocked, string[] reasons) && qIsLocked))
            lockedReason = _L("Prev quest");

        if (questFunctions.IsQuestAccepted(elementId))
            return (QstTheme.Amber, FontAwesomeIcon.PersonWalkingArrowRight, _L("Active"));
        if (elementId is QuestId questId && questFunctions.IsDailyAlliedSocietyQuestAndAvailableToday(questId))
        {
            if (!questFunctions.IsReadyToAcceptQuest(questId))
                return (QstTheme.Success, FontAwesomeIcon.Check, _L("Complete"));
            if (questFunctions.IsQuestComplete(questId))
                return (QstTheme.Info, FontAwesomeIcon.Running, _L("Available"));

            return (QstTheme.Amber, FontAwesomeIcon.Running, _L("Available"));
        }

        if (questFunctions.IsQuestAcceptedOrComplete(elementId))
            return (QstTheme.Success, FontAwesomeIcon.Check, _L("Complete"));
        if (questFunctions.IsQuestUnobtainable(elementId))
            return (QstTheme.TextMuted, FontAwesomeIcon.Minus, _L("Unobtainable"));
        if (!string.IsNullOrEmpty(lockedReason))
            return (QstTheme.Danger, FontAwesomeIcon.Times, $"{_L("Locked")}:\n  {lockedReason}");
        if (prereqValue == null)
            return (QstTheme.Info, FontAwesomeIcon.QuestionCircle, _L("Available(?)"));

        return (QstTheme.Amber, FontAwesomeIcon.Running, _L("Available"));
    }

    public static (Vector4 color, FontAwesomeIcon icon) GetInstanceStyle(ushort instanceId)
    {
        if (UIState.IsInstanceContentCompleted(instanceId))
            return (QstTheme.Success, FontAwesomeIcon.Check);
        if (UIState.IsInstanceContentUnlocked(instanceId))
            return (QstTheme.Amber, FontAwesomeIcon.Running);

        return (QstTheme.Danger, FontAwesomeIcon.Times);
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
            colorOverride ?? (complete ? QstTheme.Success : QstTheme.Danger),
            complete ? FontAwesomeIcon.Check : FontAwesomeIcon.Times);
    }
}
