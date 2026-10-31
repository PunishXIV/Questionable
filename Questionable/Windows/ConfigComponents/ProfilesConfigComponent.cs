using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class ProfilesConfigComponent
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    IObjectTable objectTable) : ConfigComponent(pluginInterface, configuration)
{
    private string _newProfileName = string.Empty;
    private string _renameTo = string.Empty;

    private static string DefaultLabel => _L("Default");

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("Profiles") + "###Profiles");
        if (!tab)
            return;

        ImGui.TextWrapped(_L("A profile lets a character use different settings. It only stores what you change while editing it; everything else follows the default settings."));
        ImGui.Separator();

        DrawCharacterProfile();
        ImGui.Separator();
        DrawEditedProfile();
        ImGui.Separator();
        DrawCreateProfile();
    }

    private void DrawCharacterProfile()
    {
        string? contentId = ProfileController.CurrentContentId;
        if (contentId == null)
        {
            ImGui.TextDisabled(_L("Log in to pick a profile for your character."));
            return;
        }

        string characterName = objectTable[0]?.Name.ToString() ?? _L("Current character");
        string[] names = [DefaultLabel, .. Configuration.Profiles.Keys.Order()];
        int index = Math.Max(0, Array.IndexOf(names, Configuration.GetProfileForCharacter(contentId) ?? DefaultLabel));
        if (ImGui.Combo(_LF("Profile for {0}", characterName), ref index, names, names.Length))
        {
            Configuration.AssignProfile(contentId, index == 0 ? null : names[index]);
            Configuration.ActivateForCharacter(contentId);
            Save();
        }
    }

    private void DrawEditedProfile()
    {
        string[] names = [DefaultLabel, .. Configuration.Profiles.Keys.Order()];
        int index = Math.Max(0, Array.IndexOf(names, Configuration.ActiveProfileName ?? DefaultLabel));
        if (ImGui.Combo(_L("Editing") + "###EditedProfile", ref index, names, names.Length))
        {
            if (index == 0)
                Configuration.ClearActiveProfile();
            else
                Configuration.ActivateProfile(names[index]);
            Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(_L("The other tabs change the profile picked here, which is also in use while this window is open. Closing the window switches back to your character's profile."));

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
            ImGui.SetTooltip(_L("Hold Ctrl to delete. Characters using this profile go back to the default settings."));
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

    private bool IsAvailableName(string name)
    {
        string trimmed = name.Trim();
        return trimmed.Length > 0 && trimmed != DefaultLabel && !Configuration.Profiles.ContainsKey(trimmed);
    }
}
