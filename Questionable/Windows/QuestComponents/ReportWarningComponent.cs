namespace Questionable.Windows.QuestComponents;

// The one-time question about sharing quest results; nothing is sent automatically until it is answered.
[RegisterSingleton]
internal sealed class ReportWarningComponent(Configuration configuration, IDalamudPluginInterface pluginInterface)
{
    public void Draw()
    {
        ImGui.TextColored(QstTheme.Accent, _L("Help fix quest paths?"));
        ImGui.TextWrapped(_L("Questionable can send a short report after each quest it runs: whether the path worked, or where and why it stopped. Everyone then sees how often each quest's path works."));
        ImGui.TextWrapped(_L("A report has the quest, the step, what went wrong, the versions of Questionable, its path data and the plugins it uses, and its last ten actions. It never has your character's name, your account or your IP address. Reports are kept for 180 days."));

        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Check, _L("Yes, share"), QstTheme.SuccessButton))
            Answer(true);
        ImGui.SameLine();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Times, _L("No thanks")))
            Answer(false);
        ImGui.SameLine();
        ImGui.TextColored(QstTheme.TextMuted, _L("You can change this in Settings > Questing."));
    }

    private void Answer(bool share)
    {
        configuration.Reports.Share = share;
        pluginInterface.SavePluginConfig(configuration);
    }
}
