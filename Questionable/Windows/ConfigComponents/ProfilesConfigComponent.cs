using Dalamud.Interface.Utility;
using Character = Questionable.External.AutoRetainerIpc.Character;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class ProfilesConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    IObjectTable objectTable,
    AutoRetainerIpc autoRetainer,
    CharacterRun characterRun,
    ConfigShare configShare) : ConfigComponent(pluginInterface, configuration)
{
    private string _newProfileName = string.Empty;
    private string _renameTo = string.Empty;
    private IReadOnlyList<Character> _characters = [];
    private DateTime _loadedAt;

    private static string DefaultLabel => _L("Default");

    public void Draw()
    {
        ImGui.TextWrapped(_L("A profile lets a character use different settings. It only stores what you change while editing it; everything else follows the default settings."));

        using (QstWidgets.TitledCard(_L("Edit a profile")))
            DrawEditedProfile();
        using (QstWidgets.TitledCard(_L("New profile")))
            DrawCreateProfile();
        using (QstWidgets.TitledCard(_L("Characters")))
            DrawCharacters();
        using (QstWidgets.TitledCard(_L("Shared settings")))
            DrawShare();
    }

    private void DrawShare()
    {
        Configuration.CharacterConfiguration settings = Configuration.Characters;
        ImGui.TextColored(QstTheme.TextMuted, _L("Copies settings from a Config Share code onto your characters: make the code with the game's Config Share on the character to copy from. This overwrites the ticked parts on each character and can't be undone."));

        bool apply = settings.ApplyShare;
        if (ImGui.Checkbox(_L("Apply to each character in the list"), ref apply))
        {
            settings.ApplyShare = apply;
            Save();
        }

        ImGuiComponents.HelpMarker(_L("Before questing on a character, Questionable downloads the code's settings once. A new code is applied again."));

        string code = settings.ShareCode;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText(_L("Share code"), ref code, 16))
        {
            settings.ShareCode = code.Trim();
            Save();
        }

        string[] labels = ConfigShare.CategoryLabels;
        for (int i = 0; i < labels.Length; i++)
        {
            uint bit = 1u << i;
            bool ticked = (settings.ShareCategories & bit) != 0;
            if (ImGui.Checkbox(labels[i] + "###ShareCategory" + i, ref ticked))
            {
                settings.ShareCategories = ticked ? settings.ShareCategories | bit : settings.ShareCategories & ~bit;
                Save();
            }
        }

        using (ImRaii.Disabled(configShare.IsBusy || !ConfigShare.IsValid(settings.ShareCode, settings.ShareCategories) ||
                               objectTable[0] == null))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Download, _L("Apply to this character now")))
                configShare.Apply();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            QstWidgets.Tooltip(_L("Needs a 16-character code and at least one ticked part."));

        if (configShare.Status is { } status)
            ImGui.TextColored(QstTheme.TextMuted, status);
    }

    private void DrawEditedProfile()
    {
        string[] names = ProfileNames();
        int index = Math.Max(0, Array.IndexOf(names, Configuration.ActiveProfileName ?? DefaultLabel));
        if (ImGui.Combo(_L("Editing") + "###EditedProfile", ref index, names, names.Length))
        {
            if (index == 0)
                Configuration.ClearActiveProfile();
            else
                Configuration.ActivateProfile(names[index]);
            Save();
        }

        ImGuiComponents.HelpMarker(_L("The other pages change the profile picked here, which is also in use while this window is open. Closing the window switches back to your character's profile."));

        if (Configuration.ActiveProfileName is not { } activeProfile)
            return;

        ImGui.InputText(_L("New name") + "###RenameProfile", ref _renameTo, 64);
        ImGui.SameLine();
        using (ImRaii.Disabled(!IsAvailableName(_renameTo)))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Pen, _L("Rename")) &&
                Configuration.RenameProfile(activeProfile, _renameTo.Trim()))
            {
                _renameTo = string.Empty;
                Save();
            }
        }

        using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Trash, _LF("Delete '{0}'", activeProfile)))
            {
                Configuration.DeleteProfile(activeProfile);
                Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            QstWidgets.Tooltip(_L("Hold CTRL to delete. Characters using this profile go back to the default settings."));
    }

    private void DrawCreateProfile()
    {
        ImGui.InputText(_L("Profile name") + "###NewProfile", ref _newProfileName, 64);
        ImGui.SameLine();
        using (ImRaii.Disabled(!IsAvailableName(_newProfileName)))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Plus, _L("Create")))
            {
                string name = _newProfileName.Trim();
                Configuration.CreateProfile(name);
                Configuration.ActivateProfile(name);
                _newProfileName = string.Empty;
                Save();
            }
        }
    }

    private void DrawCharacters()
    {
        if (ProfileController.CurrentContentId is not { } currentId)
        {
            ImGui.TextDisabled(_L("Log in to pick a profile for your character."));
            return;
        }

        if (autoRetainer.IsReady() && DateTime.Now - _loadedAt > TimeSpan.FromSeconds(2))
        {
            _characters = autoRetainer.GetCharacters();
            _loadedAt = DateTime.Now;
        }

        if (!autoRetainer.IsReady() || _characters.Count == 0)
        {
            ImGui.TextWrapped(!autoRetainer.IsReady()
                ? _L("Install and enable AutoRetainer to see all your characters and quest on several in a row.")
                : _L("AutoRetainer has no characters yet. Log in to each character once while AutoRetainer is enabled."));
            DrawTable([(currentId, objectTable[0]?.Name.ToString() ?? _L("Current character"))], canTick: false);
            return;
        }

        // Wrapping at the full width pushes the help marker past the card edge.
        using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X))
            ImGui.TextUnformatted(_L("Tick characters to quest on them in a row. Start on a ticked one; when its run ends, Questionable moves to the next. Each uses its own profile and stop conditions."));
        ImGuiComponents.HelpMarker(_L("The run-end commands on the Stopping page run for every character; logging out or closing the game only happens after the last one."));
        DrawRun();
        DrawTable([.. _characters.Select(x => (x.ContentId.ToString(CultureInfo.InvariantCulture), $"{x.Name}@{x.World}"))],
            canTick: true);
    }

    private void DrawRun()
    {
        if (characterRun.Status is not { } status)
            return;

        ImGui.TextColored(QstTheme.Accent, status);
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Times, _L("Cancel list")))
            characterRun.Cancel();
        if (ImGui.IsItemHovered())
            QstWidgets.Tooltip(_L("Stops switching characters. Questing on the current character keeps going."));
    }

    private void DrawTable(List<(string ContentId, string Name)> characters, bool canTick)
    {
        using var table = ImRaii.Table("Characters", canTick ? 3 : 2,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        if (canTick)
            ImGui.TableSetupColumn("##Ticked", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn(_L("Character"));
        ImGui.TableSetupColumn(_L("Profile"), ImGuiTableColumnFlags.WidthFixed, 170 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        string[] profiles = ProfileNames();
        if (canTick && characters.Count > 1)
            DrawAllRow(characters, profiles);

        foreach ((string contentId, string name) in characters)
        {
            using var id = ImRaii.PushId(contentId);
            ImGui.TableNextRow();

            if (canTick)
            {
                ImGui.TableNextColumn();
                ulong numericId = ulong.Parse(contentId, CultureInfo.InvariantCulture);
                bool ticked = Configuration.Characters.EnabledIds.Contains(numericId);
                if (ImGui.Checkbox("##Ticked", ref ticked))
                {
                    if (ticked)
                        Configuration.Characters.EnabledIds.Add(numericId);
                    else
                        Configuration.Characters.EnabledIds.Remove(numericId);
                    Save();
                }
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (contentId == ProfileController.CurrentContentId)
                ImGui.TextColored(QstTheme.Accent, name);
            else
                ImGui.TextUnformatted(name);

            ImGui.TableNextColumn();
            int index = Math.Max(0, Array.IndexOf(profiles, Configuration.GetProfileForCharacter(contentId) ?? DefaultLabel));
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

    private void DrawAllRow(List<(string ContentId, string Name)> characters, string[] profiles)
    {
        using var id = ImRaii.PushId("All");
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        List<ulong> ids = [.. characters.Select(x => ulong.Parse(x.ContentId, CultureInfo.InvariantCulture))];
        bool all = ids.All(Configuration.Characters.EnabledIds.Contains);
        if (ImGui.Checkbox("##Ticked", ref all))
        {
            Configuration.Characters.EnabledIds.RemoveAll(ids.Contains);
            if (all)
                Configuration.Characters.EnabledIds.AddRange(ids);
            Save();
        }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(QstTheme.TextMuted, _L("All characters"));

        ImGui.TableNextColumn();
        string[] picked = [.. characters.Select(x => Configuration.GetProfileForCharacter(x.ContentId) ?? DefaultLabel).Distinct()];
        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##Profile", picked.Length == 1 ? picked[0] : _L("Mixed"));
        if (!combo)
            return;

        foreach (string profile in profiles)
        {
            if (!ImGui.Selectable(profile, picked.Length == 1 && picked[0] == profile))
                continue;

            foreach ((string contentId, _) in characters)
                Configuration.AssignProfile(contentId, profile == DefaultLabel ? null : profile);
            if (ProfileController.CurrentContentId is { } current && characters.Any(x => x.ContentId == current))
                Configuration.ActivateForCharacter(current);
            Save();
        }
    }

    private string[] ProfileNames() => [DefaultLabel, .. Configuration.Profiles.Keys.Order()];

    private bool IsAvailableName(string name)
    {
        string trimmed = name.Trim();
        return trimmed.Length > 0 && trimmed != DefaultLabel && !Configuration.Profiles.ContainsKey(trimmed);
    }
}
