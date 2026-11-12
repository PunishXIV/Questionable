using Dalamud.Interface.Utility;
using Character = Questionable.External.AutoRetainerIpc.Character;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class CharactersConfigComponent(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    AutoRetainerIpc autoRetainer,
    CharacterRun characterRun) : ConfigComponent(pluginInterface, configuration)
{
    private IReadOnlyList<Character> _characters = [];
    private DateTime _loadedAt;

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Characters") + "###Characters");
        if (!tab)
            return;

        ImGui.TextWrapped(_L("Run automatic questing on several characters in a row. Questionable goes down the ticked characters; when one character's run ends, it switches to the next. Each character uses its own profile, including its stop conditions."));
        ImGui.Separator();

        if (!autoRetainer.IsReady())
        {
            ImGui.TextColored(QstTheme.Amber, _L("Install and enable AutoRetainer to load the character list."));
            return;
        }

        if (DateTime.Now - _loadedAt > TimeSpan.FromSeconds(2))
        {
            _characters = autoRetainer.GetCharacters();
            _loadedAt = DateTime.Now;
        }

        if (_characters.Count == 0)
        {
            ImGui.TextWrapped(_L("AutoRetainer has no characters yet. Log in to each character once while AutoRetainer is enabled."));
            return;
        }

        DrawRun();
        DrawCharacters();
    }

    private void DrawRun()
    {
        if (characterRun.Status is { } status)
        {
            ImGui.TextColored(QstTheme.Accent, status);
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Times, _L("Cancel list")))
                characterRun.Cancel();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Stops switching characters. Questing on the current character keeps going."));
            return;
        }

        int ticked = _characters.Count(x => Configuration.Characters.EnabledIds.Contains(x.ContentId));
        using (ImRaii.Disabled(ticked == 0 || !characterRun.CanStart))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Users,
                    _LF("Start on {0} ticked character(s)", ticked)))
                characterRun.Start();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(_L("Starts with this character if it is ticked, then goes down the list.\nThe run-end commands on the Stop tab run for every character; logging out or closing the game only happens after the last one."));
    }

    private void DrawCharacters()
    {
        using var table = ImRaii.Table("Characters", 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("##Ticked", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn(_L("Character"));
        ImGui.TableSetupColumn(_L("Profile"), ImGuiTableColumnFlags.WidthFixed, 170 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        string[] profiles = [_L("Default"), .. Configuration.Profiles.Keys.Order()];
        foreach (Character character in _characters)
        {
            string contentId = character.ContentId.ToString(CultureInfo.InvariantCulture);
            using var id = ImRaii.PushId(contentId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            bool ticked = Configuration.Characters.EnabledIds.Contains(character.ContentId);
            if (ImGui.Checkbox("##Ticked", ref ticked))
            {
                if (ticked)
                    Configuration.Characters.EnabledIds.Add(character.ContentId);
                else
                    Configuration.Characters.EnabledIds.Remove(character.ContentId);
                Save();
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"{character.Name}@{character.World}");

            ImGui.TableNextColumn();
            int index = Math.Max(0, Array.IndexOf(profiles, Configuration.GetProfileForCharacter(contentId) ?? profiles[0]));
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##Profile", ref index, profiles, profiles.Length))
            {
                Configuration.AssignProfile(contentId, index == 0 ? null : profiles[index]);
                if (contentId == ProfileController.CurrentContentId)
                    Configuration.ActivateForCharacter(contentId);
                Save();
            }
        }
    }
}
