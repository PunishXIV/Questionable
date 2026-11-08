using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Questionable.Windows.Common.Ui;
namespace Questionable.Windows.ConfigComponents;

internal abstract class ConfigComponent(IDalamudPluginInterface pluginInterface, Configuration configuration)
{
    private const char DutyClipboardSeparator = ';';
    private const char DutyWhitelistPrefix = '+';
    private const char DutyBlacklistPrefix = '-';

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

    // Invalid saved values reset to the first option.
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

    protected bool DrawCheckbox(string label, Func<bool> get, Action<bool> set, string? help = null)
    {
        bool value = get();
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            Save();
        }

        if (help != null)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(help);
        }

        return value;
    }

    protected static string FormatLevel(int level, bool includePrefix = true)
    {
        if (level == 0)
            return string.Empty;

        return $"{(includePrefix ? SeIconChar.LevelEn.ToIconString() : string.Empty)}{FormatLevel(level / 10, includePrefix: false)}{(SeIconChar.Number0 + level % 10).ToIconChar()}";
    }

    protected static void DrawNotes(bool enabledByDefault, IEnumerable<string> notes)
    {
        using ImRaii.ColorDisposable color = ImRaii.PushColor(ImGuiCol.TextDisabled, !enabledByDefault ? QstTheme.Amber : QstTheme.Info);

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

        ImGui.TextColored(QstTheme.Amber,
            _L("While testing, the following issues have been found:"));
        foreach (string note in notes)
            ImGui.BulletText(note);
    }

    protected void DrawClipboardButtons(string prefix, HashSet<uint> whitelisted, HashSet<uint> blacklisted)
    {
        using (ImRaii.Disabled(whitelisted.Count + blacklisted.Count == 0))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Copy, _L("Export to clipboard")))
                ImGui.SetClipboardText(ExportCfcIds(prefix, whitelisted, blacklisted));
        }

        ImGui.SameLine();

        string clipboardText = ImGui.GetClipboardText().Trim();
        using (ImRaii.Disabled(string.IsNullOrEmpty(clipboardText) ||
                               !clipboardText.StartsWith(prefix, StringComparison.InvariantCulture)))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Paste, _L("Import from Clipboard")))
            {
                ImportCfcIds(clipboardText.Substring(prefix.Length), whitelisted, blacklisted);
                Save();
            }
        }
    }

    protected void DrawResetButton(HashSet<uint> whitelisted, HashSet<uint> blacklisted)
    {
        using (ImRaii.Disabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl)))
        {
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Undo, _L("Reset to default")))
            {
                whitelisted.Clear();
                blacklisted.Clear();
                Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(_L("Hold CTRL to enable this button."));
    }

    internal static string ExportCfcIds(string prefix, HashSet<uint> whitelisted, HashSet<uint> blacklisted)
    {
        IEnumerable<string> white = whitelisted.Select(x => $"{DutyWhitelistPrefix}{x}");
        IEnumerable<string> black = blacklisted.Select(x => $"{DutyBlacklistPrefix}{x}");
        return prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Join(DutyClipboardSeparator, white.Concat(black))));
    }

    internal static void ImportCfcIds(string encoded, HashSet<uint> whitelisted, HashSet<uint> blacklisted)
    {
        string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

        whitelisted.Clear();
        blacklisted.Clear();
        foreach (string part in text.Split(DutyClipboardSeparator))
        {
            if (part.StartsWith(DutyWhitelistPrefix) &&
                uint.TryParse(part.AsSpan(1), CultureInfo.InvariantCulture, out uint whitelistedCfcId))
            {
                whitelisted.Add(whitelistedCfcId);
            }

            if (part.StartsWith(DutyBlacklistPrefix) &&
                uint.TryParse(part.AsSpan(1), CultureInfo.InvariantCulture, out uint blacklistedCfcId))
            {
                blacklisted.Add(blacklistedCfcId);
            }
        }
    }
}
