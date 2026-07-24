using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
namespace Questionable.Windows.ConfigComponents;

internal abstract class ConfigComponent(IDalamudPluginInterface pluginInterface, Configuration configuration)
{
    protected const char DutyClipboardSeparator = ';';
    protected const char DutyWhitelistPrefix = '+';
    protected const char DutyBlacklistPrefix = '-';

    private readonly IDalamudPluginInterface _pluginInterface = pluginInterface;

    protected readonly string[] SupportedCfcOptions =
    [
        $"{SeIconChar.Circle.ToIconChar()} " + _L("Enabled (Default)"),
        $"{SeIconChar.Circle.ToIconChar()} " + _L("Enabled"),
        $"{SeIconChar.Cross.ToIconChar()} " + _L("Disabled")
    ];

    protected readonly string[] UnsupportedCfcOptions =
    [
        $"{SeIconChar.Cross.ToIconChar()} " + _L("Disabled (Default)"),
        $"{SeIconChar.Circle.ToIconChar()} " + _L("Enabled"),
        $"{SeIconChar.Cross.ToIconChar()} " + _L("Disabled")
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

    protected static string FormatLevel(int level, bool includePrefix = true)
    {
        if (level == 0)
            return string.Empty;

        return $"{(includePrefix ? SeIconChar.LevelEn.ToIconString() : string.Empty)}{FormatLevel(level / 10, includePrefix: false)}{(SeIconChar.Number0 + level % 10).ToIconChar()}";
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
            _L("While testing, the following issues have been found:"));
        foreach (string note in notes)
            ImGui.BulletText(note);
    }
}
