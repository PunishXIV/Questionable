namespace Questionable.Windows.Common;

public abstract class LWindow(string windowName, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false) : Window(windowName, flags, forceMainWindow)
{
    protected const int TitleBarButtonPriority = -100;

    protected static readonly Vector2 TitleBarIconOffset = new(2, 2);

    private static readonly (string Name, string Url)[] Contributors =
    [
        ("aly", "https://ko-fi.com/alydev"),
        ("Kagekazu", "https://ko-fi.com/kagekazu"),
    ];

    private bool _openCredits;

    protected void AddSponsorButton() => TitleBarButtons.Add(new()
    {
        Icon = FontAwesomeIcon.Heart,
        IconOffset = TitleBarIconOffset,
        Click = _ => _openCredits = true,
        Priority = TitleBarButtonPriority,
        ShowTooltip = () => QstWidgets.Tooltip(_L("Who makes Questionable")),
    });

    private void DrawCredits()
    {
        if (_openCredits)
        {
            ImGui.OpenPopup("##Credits");
            _openCredits = false;
        }

        using ImRaii.PopupDisposable popup = ImRaii.Popup("##Credits");
        if (!popup)
            return;

        ImGui.TextUnformatted(_L("Questionable is written by:"));
        foreach ((string name, string url) in Contributors)
            if (ImGuiComponentsLocal.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, name))
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private bool _initializedConfig;
    private bool _wasCollapsedLastFrame;
    private QstTheme.WindowStyleScope? _windowStyle;

    protected bool ClickedHeaderLastFrame { get; private set; }
    protected bool ClickedHeaderCurrentFrame { get; private set; }
    protected bool UncollapseNextFrame { get; set; }

    public bool IsOpenAndUncollapsed
    {
        get => IsOpen && !_wasCollapsedLastFrame;
        set
        {
            IsOpen = value;
            UncollapseNextFrame = value;
        }
    }

    protected new bool IsPinned { get => AllowPinning; set => AllowPinning = value; }

    protected new bool IsClickthrough { get => AllowClickthrough; set => AllowClickthrough = value; }

    protected int? Alpha
    {
        get
        {
            float? value = BgAlpha;
            return (int?)(10_0000 * value);
        }
        set => BgAlpha = value / 10_0000f;
    }

    private void LoadWindowConfig()
    {
        if (this is IPersistableWindowConfig persistable)
        {
            WindowConfig? config = persistable.WindowConfig;
            if (config != null)
            {
                if (AllowPinning)
                    IsPinned = config.IsPinned;

                if (AllowClickthrough)
                    IsClickthrough = config.IsClickthrough;

                Alpha = config.Alpha;
            }

            _initializedConfig = true;
        }
    }

    private void UpdateWindowConfig()
    {
        if (this is IPersistableWindowConfig persistable && !ImGui.IsAnyMouseDown())
        {
            WindowConfig? config = persistable.WindowConfig;
            if (config != null)
            {
                bool changed = false;
                if (AllowPinning && config.IsPinned != IsPinned)
                {
                    config.IsPinned = IsPinned;
                    changed = true;
                }

                if (AllowClickthrough && config.IsClickthrough != IsClickthrough)
                {
                    config.IsClickthrough = IsClickthrough;
                    changed = true;
                }

                if (config.Alpha != Alpha)
                {
                    config.Alpha = Alpha;
                    changed = true;
                }

                if (changed)
                    persistable.SaveWindowConfig();
            }
        }
    }

    public void ToggleOrUncollapse() => IsOpenAndUncollapsed ^= true;

    public override void OnOpen()
    {
        UncollapseNextFrame = true;
        base.OnOpen();
    }

    public override void Update() => _wasCollapsedLastFrame = true;

    public override void PreDraw()
    {
        _windowStyle = QstTheme.PushWindowStyle();

        if (!_initializedConfig)
            LoadWindowConfig();

        if (UncollapseNextFrame)
        {
            ImGui.SetNextWindowCollapsed(collapsed: false);
            UncollapseNextFrame = false;
        }

        base.PreDraw();

        ClickedHeaderLastFrame = ClickedHeaderCurrentFrame;
        ClickedHeaderCurrentFrame = false;
    }

    public sealed override void Draw()
    {
        _wasCollapsedLastFrame = false;

        DrawContent();
        DrawCredits();
    }

    public abstract void DrawContent();

    public override void PostDraw()
    {
        _windowStyle?.Dispose();
        _windowStyle = null;

        base.PostDraw();

        if (_initializedConfig)
            UpdateWindowConfig();
    }
}
