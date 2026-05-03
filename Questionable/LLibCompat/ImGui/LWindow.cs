using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Diagnostics.CodeAnalysis;
namespace LLib.ImGui;

[SuppressMessage("ReSharper", "SuspiciousTypeConversion.Global")]
public abstract class LWindow : Window
{
    private bool _initializedConfig;
    private bool _wasCollapsedLastFrame;

    protected LWindow(string windowName, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)
        : base(windowName, flags, forceMainWindow)
    {
    }

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
        if (this is IPersistableWindowConfig pwc)
        {
            WindowConfig? config = pwc.WindowConfig;
            if (config != null)
            {
                if (AllowPinning)
                {
                    IsPinned = config.IsPinned;
                }

                if (AllowClickthrough)
                {
                    IsClickthrough = config.IsClickthrough;
                }

                Alpha = config.Alpha;
            }

            _initializedConfig = true;
        }
    }

    private void UpdateWindowConfig()
    {
        if (this is IPersistableWindowConfig pwc && !Dalamud.Bindings.ImGui.ImGui.IsAnyMouseDown())
        {
            WindowConfig? config = pwc.WindowConfig;
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
                {
                    pwc.SaveWindowConfig();
                }
            }
        }
    }

    public void ToggleOrUncollapse()
    {
        IsOpenAndUncollapsed ^= true;
    }

    public override void OnOpen()
    {
        UncollapseNextFrame = true;
        base.OnOpen();
    }

    public override void Update()
    {
        _wasCollapsedLastFrame = true;
    }

    public override void PreDraw()
    {
        if (!_initializedConfig)
        {
            LoadWindowConfig();
        }

        if (UncollapseNextFrame)
        {
            Dalamud.Bindings.ImGui.ImGui.SetNextWindowCollapsed(false);
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
    }

    public abstract void DrawContent();

    public override void PostDraw()
    {
        base.PostDraw();

        if (_initializedConfig)
        {
            UpdateWindowConfig();
        }
    }
}
