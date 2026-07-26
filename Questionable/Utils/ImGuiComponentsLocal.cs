using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace Questionable.Utils;

internal static class ImGuiComponentsLocal
{
    internal static bool IconButton(string id, FontAwesomeIcon icon, Vector4? defaultColor = null,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        return IconButton(icon, defaultColor: null, activeColor: null, hoveredColor: null, size: null, file, line, id);
    }
    internal static bool IconButton(FontAwesomeIcon icon,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        return IconButton(icon, defaultColor: null, activeColor: null, hoveredColor: null, size: null, file, line);
    }
    internal static bool IconButton(FontAwesomeIcon icon, Vector2 size,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        return IconButton(icon, defaultColor: null, activeColor: null, hoveredColor: null, size, file, line);
    }
    internal static bool IconButton(FontAwesomeIcon icon,
                                    Vector4? defaultColor,
                                    Vector4? activeColor = null,
                                    Vector4? hoveredColor = null,
                                    Vector2? size = null,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0,
                                    string? id = null)
    {
        id ??= $"{Path.GetFileName(file)}:{line}";
        ImGui.PushID(id);
        bool result = ImGuiComponents.IconButton(icon, defaultColor, activeColor, hoveredColor, size);
        ImGui.PopID();
        return result;
    }

    internal static bool IconButtonWithText(FontAwesomeIcon icon,
                                            string text,
                                            Vector2 size,
                           [CallerFilePath] string file = "",
                         [CallerLineNumber] int line = 0)
    {
        return IconButtonWithText(icon, text, defaultColor: null, activeColor: null, hoveredColor: null, size, file, line);
    }

    internal static bool IconButtonWithText(FontAwesomeIcon icon,
                                            string text,
                                            Vector4? defaultColor = null,
                                            Vector4? activeColor = null,
                                            Vector4? hoveredColor = null,
                                            Vector2? size = null,
                           [CallerFilePath] string file = "",
                         [CallerLineNumber] int line = 0,
                                    string? id = null)
    {
        id ??= $"{Path.GetFileName(file)}:{line}";
        ImGui.PushID(id);
        bool result = ImGuiComponents.IconButtonWithText(icon, text, defaultColor, activeColor, hoveredColor, size);
        ImGui.PopID();
        return result;
    }

    /// <summary>
    ///     Draws a searchable combo (BeginCombo + InputTextWithHint filter) for large option lists.
    ///     The search box stays pinned at the top of the popup; only the option list scrolls.
    /// </summary>
    internal static bool DrawSearchableCombo<T>(
        string label, T[] values, string[] labels, T active, ref string searchString, ref T selected,
        bool labelAsPreview = false,
                           [CallerFilePath] string file = "",
                         [CallerLineNumber] int line = 0)
    {
        if (values.Length == 0)
            return false;

        int index = Array.IndexOf(values, active);
        if (index == -1)
        {
            index = 0;
            selected = values[index];
        }

        string preview = labels[index];
        if (labelAsPreview)
            preview = label;
        else
        {
            var size = ImGui.GetWindowContentRegionMax();
            ImGui.SetNextItemWidth(size.X / 2);
        }
        using var combo = ImRaii.Combo($"{(!labelAsPreview ? label : "")}##SearchableCombo:{Path.GetFileName(file)}:{line}", preview, ImGuiComboFlags.HeightLarge);
        if (!combo)
            return false;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##filter", "Search...", ref searchString, 256);

        // The option list lives in its own fixed-height scrollable child so the search box above
        // stays pinned and visible; SetItemDefaultFocus() then scrolls the child, not the popup.
        int visibleRows = Math.Clamp(labels.Length, 1, 12);
        var listSize = ImGui.GetContentRegionAvail() with { Y = ImGui.GetTextLineHeightWithSpacing() * visibleRows };
        using (var child = ImRaii.Child("##searchableComboList", listSize))
        {
            if (child)
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    if (!string.IsNullOrEmpty(searchString) &&
                        !labels[i].Contains(searchString, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    if (labels[i].StartsWith("##D"))
                    {
                        ImGui.TextDisabled(labels[i].Substring(3));
                        continue;
                    }
                    if (labels[i].StartsWith("##S"))
                    {
                        ImGui.Separator();
                        continue;
                    }

                    bool isSelected = i == index;
                    if (ImGui.Selectable(labels[i], isSelected))
                    {
                        selected = values[i];
                        searchString = string.Empty;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
            }
        }

        return true;
    }

    public static void HelpMarker(string helpText, string[]? bullets) => HelpMarker(helpText, FontAwesomeIcon.InfoCircle, bullets: bullets);

    public static void HelpMarker(string helpText, FontAwesomeIcon icon, Vector4? color = null, string[]? bullets = null)
    {
        using var col = ImRaii.PushColor(ImGuiCol.TextDisabled, color);

        ImGui.SameLine();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextDisabled(icon.ToIconString());
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                using (ImRaii.TextWrapPos(ImGui.GetFontSize() * 35.0f))
                {
                    ImGui.Text(helpText);
                    if (bullets != null)
                        foreach (string point in bullets)
                            ImGui.BulletText(point);
                }
            }
        }
    }

    public static bool QuestNotice(QuestController questController, Quest quest, string? label = null)
    {
        if (IconButton(FontAwesomeIcon.Play))
        {
            questController.SetNextQuest(quest);
            questController.Start(_L("QuestNotice"));
        }

        bool hovered = ImGui.IsItemHovered();

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label ?? quest.Info.Name);
        return hovered | ImGui.IsItemHovered();
    }
}
