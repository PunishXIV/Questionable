using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

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

    protected bool IsPinned
    {
        get => InternalIsPinned(this);
        set => InternalIsPinned(this) = value;
    }

    protected bool IsClickthrough
    {
        get => InternalIsClickthrough(this);
        set => InternalIsClickthrough(this) = value;
    }

    protected int? Alpha
    {
        get
        {
            float? value = InternalAlpha(this);
            return (int?)(10_0000 * value);
        }
        set => InternalAlpha(this) = value / 10_0000f;
    }

    private void LoadWindowConfig()
    {
        if (this is IPersistableWindowConfig pwc)
        {
            WindowConfig? config = pwc.WindowConfig;
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

    #region Dalamud Overrides
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
            LoadWindowConfig();

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
        // executed after update
        _wasCollapsedLastFrame = false;

        DrawContent();
    }

    public abstract void DrawContent();

    public override void PostDraw()
    {
        base.PostDraw();

        if (_initializedConfig)
            UpdateWindowConfig();
    }
    #endregion

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "internalIsPinned")]
    private static extern ref bool InternalIsPinned(Window @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "internalIsClickthrough")]
    private static extern ref bool InternalIsClickthrough(Window @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "internalAlpha")]
    private static extern ref float? InternalAlpha(Window @this);
}
