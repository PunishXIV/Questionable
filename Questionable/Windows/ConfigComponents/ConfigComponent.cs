using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
namespace Questionable.Windows.ConfigComponents;

internal abstract class ConfigComponent(IDalamudPluginInterface pluginInterface, Configuration configuration)
{
    protected const string DutyClipboardSeparator = ";";
    protected const string DutyWhitelistPrefix = "+";
    protected const string DutyBlacklistPrefix = "-";

    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;

    protected readonly string[] SupportedCfcOptions =
    [
        $"{SeIconChar.Circle.ToIconChar()} Enabled (Default)",
        $"{SeIconChar.Circle.ToIconChar()} Enabled",
        $"{SeIconChar.Cross.ToIconChar()} Disabled"
    ];

    protected readonly string[] UnsupportedCfcOptions =
    [
        $"{SeIconChar.Cross.ToIconChar()} Disabled (Default)",
        $"{SeIconChar.Circle.ToIconChar()} Enabled",
        $"{SeIconChar.Cross.ToIconChar()} Disabled"
    ];

    protected Configuration Configuration { get; } = configuration;

    public abstract void DrawTab();

    protected void Save() => _pluginInterface.SavePluginConfig(Configuration);

    /// <summary>
    ///     Draws an ImGui combo that maps a configuration value to/from an entry in <paramref name="values"/>.
    ///     If the current value is not in <paramref name="values"/>, resets to <paramref name="values"/>[0] and saves.
    /// </summary>
    protected void DrawComboOption<T>(string label, T[] values, string[] labels, Func<T> get, Action<T> set)
    {
        if (values.Length == 0)
            return;

        int index = Array.IndexOf(values, get());
        if (index == -1)
        {
            index = 0;
            set(values[index]);
            Save();
        }

        if (ImGui.Combo(label, ref index, labels, labels.Length))
        {
            set(values[index]);
            Save();
        }
    }
    //TODO make the search option show up. now u need to scroll up to make it appear 
    /// <summary>
    ///     Draws a searchable combo (BeginCombo + InputTextWithHint filter) for large option lists.
    /// </summary>
    protected void DrawSearchableCombo<T>(string label, T[] values, string[] labels, Func<T> get, Action<T> set,
        ref string searchString)
    {
        if (values.Length == 0)
            return;

        int index = Array.IndexOf(values, get());
        if (index == -1)
        {
            index = 0;
            set(values[index]);
            Save();
        }

        string preview = labels[index];
        if (ImGui.BeginCombo(label, preview, ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##filter", "Search...", ref searchString, 256);

            for (int i = 0; i < labels.Length; i++)
            {
                if (!string.IsNullOrEmpty(searchString) &&
                    !labels[i].Contains(searchString, StringComparison.CurrentCultureIgnoreCase))
                    continue;

                bool isSelected = i == index;
                if (ImGui.Selectable(labels[i], isSelected))
                {
                    set(values[i]);
                    Save();
                    searchString = string.Empty;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
    }

    protected static string FormatLevel(int level, bool includePrefix = true)
    {
        if (level == 0)
            return string.Empty;

        return $"{(includePrefix ? SeIconChar.LevelEn.ToIconString() : string.Empty)}{FormatLevel(level / 10, false)}{(SeIconChar.Number0 + level % 10).ToIconChar()}";
    }

    protected static void DrawNotes(bool enabledByDefault, IEnumerable<string> notes)
    {
        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.TextDisabled, !enabledByDefault ? ImGuiColors.DalamudYellow : ImGuiColors.ParsedBlue);

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (!enabledByDefault)
                ImGui.TextDisabled(FontAwesomeIcon.ExclamationTriangle.ToIconString());
            else
                ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
        }

        if (!ImGui.IsItemHovered())
            return;

        using ImRaii.TooltipDisposable _ = ImRaii.Tooltip();

        ImGui.TextColored(ImGuiColors.DalamudYellow,
            "While testing, the following issues have been found:");
        foreach (string note in notes)
            ImGui.BulletText(note);
    }
}
