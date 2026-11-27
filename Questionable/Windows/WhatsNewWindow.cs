using Dalamud.Interface.Utility;

namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class WhatsNewWindow : LWindow
{
    private const float TextWidth = 460f;

    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;
    private IReadOnlyList<WhatsNew.Entry> _entries;

    public WhatsNewWindow(Configuration configuration, IDalamudPluginInterface pluginInterface)
        : base(_L("What's new in Questionable") + "###QuestionableWhatsNew",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)
    {
        _configuration = configuration;
        _pluginInterface = pluginInterface;
        _entries = WhatsNew.Unseen(WhatsNew.Entries, configuration.WhatsNewVersion);

        AllowPinning = false;
        AllowClickthrough = false;
        // Setup comes first; anything unseen waits for the next start.
        IsOpen = configuration.IsPluginSetupComplete() && _entries.Count > 0;
    }

    // /qst whatsnew: every entry, not only the unseen ones.
    internal void ShowAll()
    {
        _entries = WhatsNew.Entries;
        IsOpenAndUncollapsed = true;
    }

    public override void DrawContent()
    {
        float width = TextWidth * ImGuiHelpers.GlobalScale;
        Wrapped(_L("Settings that changed in recent updates. Keep them or switch them back here; you can change them later in the settings too."),
            width, QstTheme.TextMuted);

        foreach (IGrouping<Version, WhatsNew.Entry> version in _entries.GroupBy(x => x.Version).OrderByDescending(x => x.Key))
        {
            using (QstWidgets.TitledCard(_LF("Version {0}", version.Key)))
            {
                foreach (WhatsNew.Entry entry in version)
                    DrawEntry(entry, width);
            }
        }

        ImGui.Spacing();
        if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.Check, _L("Got it")))
            IsOpen = false;
    }

    // Closing the window any way counts as seen, so it does not come back every start.
    public override void OnClose()
    {
        _configuration.WhatsNewVersion = WhatsNew.Current;
        _pluginInterface.SavePluginConfig(_configuration);
    }

    private void DrawEntry(WhatsNew.Entry entry, float width)
    {
        ImGui.Spacing();
        Wrapped(entry.Title(), width, QstTheme.Text);
        Wrapped(entry.Text(), width, QstTheme.TextMuted);

        if (entry.Toggle is not { } toggle)
            return;

        bool value = toggle.Get(_configuration);
        if (ImGui.Checkbox(toggle.Label(), ref value))
        {
            toggle.Set(_configuration, value);
            _pluginInterface.SavePluginConfig(_configuration);
        }
    }

    private static void Wrapped(string text, float width, Vector4 color)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }
}
