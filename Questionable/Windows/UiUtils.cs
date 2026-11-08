using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class UiUtils(
    QuestFunctions questFunctions,
    IDalamudPluginInterface pluginInterface,
    QuestData questData,
    GameIcons gameIcons)
{
    public unsafe (Vector4 Color, FontAwesomeIcon Icon, string Status) GetQuestStyle(ElementId elementId)
    {
        string lockedReason = string.Empty;
        IQuestInfo? info = null;
        questFunctions.prereqCache.TryGetValue(elementId.Value, out var prerequisites);
        if (questFunctions.IsQuestLocked(elementId) is (bool isLocked, string[] reasons) && isLocked)
            lockedReason = string.Join("\n  ", reasons);
        else if (questData.TryGetQuestInfo(elementId, out info) && info is IQuestInfo questInfo &&
                questInfo.PreviousQuestJoin is EQuestJoin.All)
        {
            List<string> blockers = [];
            if (prerequisites != null)
                foreach (IQuestInfo previous in prerequisites)
                    if (questFunctions.IsQuestLocked(previous.QuestId) is (bool previousLocked, string[] previousReasons) && previousLocked &&
                        !questFunctions.IsQuestUnobtainable(previous.QuestId))
                        foreach (string reason in previousReasons)
                            blockers.Add($"{previous.QuestId.Value}: {reason}");
            if (blockers.Count != 0)
                lockedReason = _L("Prev quest") + $" ({blockers[0]}{(blockers.Count > 1 ? $" +{blockers.Count - 1}" : "")})";
        }

        if (questFunctions.IsQuestAccepted(elementId))
            return (QstTheme.Amber, FontAwesomeIcon.PersonWalkingArrowRight, _L("Active"));
        if (elementId is QuestId questId && questFunctions.IsSocietyDailyAvailable(questId))
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
            return (QstTheme.Danger, FontAwesomeIcon.Lock, $"{_L("Locked")}:\n  {lockedReason}");
        if (prerequisites == null)
            return (QstTheme.Info, FontAwesomeIcon.QuestionCircle, _L("Available(?)"));

        return (QstTheme.Amber, FontAwesomeIcon.Running, _L("Available"));
    }

    public static (Vector4 color, FontAwesomeIcon icon) GetInstanceStyle(ushort instanceId)
    {
        if (UIState.IsInstanceContentCompleted(instanceId))
            return (QstTheme.Success, FontAwesomeIcon.Check);
        if (UIState.IsInstanceContentUnlocked(instanceId))
            return (QstTheme.Amber, FontAwesomeIcon.Running);

        return (QstTheme.Danger, FontAwesomeIcon.Lock);
    }

    public bool DrawQuestRow(ElementId questId, string name)
    {
        (Vector4 color, FontAwesomeIcon icon, string _) = GetQuestStyle(questId);
        bool hovered;
        using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(color, icon.ToIconString());
            hovered = ImGui.IsItemHovered();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(name);
        hovered |= ImGui.IsItemHovered();
        return hovered;
    }

    public bool ChecklistItem(string text, Vector4 color, FontAwesomeIcon icon, float extraPadding = 0, uint? iconOverride = null, Action? onClick = null)
    {
        if (extraPadding > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + extraPadding);
        if (iconOverride is not { } iconId || !gameIcons.DrawInline(iconId))
        {
            using (pluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                ImGui.TextColored(color, icon.ToIconString());
            }
        }

        bool hover = ImGui.IsItemHovered();
        bool click = ImGui.IsItemClicked();

        ImGui.SameLine();
        if (extraPadding > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + extraPadding);
        ImGui.TextUnformatted(text);
        hover |= ImGui.IsItemHovered();
        click |= ImGui.IsItemClicked();
        if (click)
            onClick?.Invoke();
        return hover;
    }

    public bool ChecklistItem(string text, bool complete, Vector4? colorOverride = null)
    {
        return ChecklistItem(text,
            colorOverride ?? (complete ? QstTheme.Success : QstTheme.Danger),
            complete ? FontAwesomeIcon.Check : FontAwesomeIcon.Times);
    }
}
