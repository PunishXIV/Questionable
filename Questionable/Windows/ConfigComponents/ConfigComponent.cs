using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using System.Collections.Generic;
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

    protected void Save()
    {
        _pluginInterface.SavePluginConfig(Configuration);
    }

    protected static string FormatLevel(int level, bool includePrefix = true)
    {
        if (level == 0)
        {
            return string.Empty;
        }

        return $"{(includePrefix ? SeIconChar.LevelEn.ToIconString() : string.Empty)}{FormatLevel(level / 10, false)}{(SeIconChar.Number0 + level % 10).ToIconChar()}";
    }

    protected static void DrawNotes(bool enabledByDefault, IEnumerable<string> notes)
    {
        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.TextDisabled, !enabledByDefault ? ImGuiColors.DalamudYellow : ImGuiColors.ParsedBlue);

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (!enabledByDefault)
            {
                ImGui.TextDisabled(FontAwesomeIcon.ExclamationTriangle.ToIconString());
            }
            else
            {
                ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
            }
        }

        if (!ImGui.IsItemHovered())
        {
            return;
        }

        using ImRaii.TooltipDisposable _ = ImRaii.Tooltip();

        ImGui.TextColored(ImGuiColors.DalamudYellow,
            "While testing, the following issues have been found:");
        foreach(string note in notes)
        {
            ImGui.BulletText(note);
        }
    }
}
