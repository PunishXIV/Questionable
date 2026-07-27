using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Questionable.Windows.Common.Ui;

internal static class QstTheme
{
    public static readonly Vector4 Accent = Rgb(255, 148, 68);
    public static readonly Vector4 AccentActive = Rgb(255, 167, 94);
    public static readonly Vector4 Danger = Rgb(255, 81, 110);
    public static readonly Vector4 Amber = Rgb(255, 176, 102);
    public static readonly Vector4 Success = Rgb(126, 212, 145);
    public static readonly Vector4 Info = Rgb(111, 174, 230);
    public static readonly Vector4 Special = Rgb(181, 144, 232);
    public static readonly Vector4 Text = Rgb(224, 224, 224);
    public static readonly Vector4 TextMuted = Rgb(156, 163, 175);
    public static readonly Vector4 TextFaint = Rgb(117, 123, 134);

    public static readonly Vector4 PanelBg = Rgba(21, 21, 21, 0.96f);
    public static readonly Vector4 PanelDark = Rgb(14, 14, 14);
    public static readonly Vector4 InputBg = Rgb(19, 19, 19);
    public static readonly Vector4 Raised = Rgb(56, 56, 56);
    public static readonly Vector4 RaisedHovered = Rgb(72, 72, 72);
    public static readonly Vector4 RaisedActive = Rgb(88, 88, 88);
    public static readonly Vector4 Edge = Rgb(64, 64, 64);

    public static Vector4 WithAlpha(Vector4 color, float alpha) => color with { W = alpha };

    public static uint ToU32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static WindowStyleScope PushWindowStyle() => new();

    internal sealed class WindowStyleScope : IDisposable
    {
        private readonly ImRaii.ColorDisposable _colors;
        private readonly ImRaii.StyleDisposable _styles;

        internal WindowStyleScope()
        {
            _colors = ImRaii.PushColor(ImGuiCol.WindowBg, PanelBg)
                .Push(ImGuiCol.PopupBg, Rgba(22, 22, 22, 0.98f))
                .Push(ImGuiCol.Border, Edge)
                .Push(ImGuiCol.Separator, Edge)
                .Push(ImGuiCol.FrameBg, InputBg)
                .Push(ImGuiCol.FrameBgHovered, Rgb(31, 31, 31))
                .Push(ImGuiCol.FrameBgActive, Raised)
                .Push(ImGuiCol.TitleBg, PanelDark)
                .Push(ImGuiCol.TitleBgActive, Rgb(26, 26, 26))
                .Push(ImGuiCol.TitleBgCollapsed, Rgba(14, 14, 14, 0.8f))
                .Push(ImGuiCol.Button, Raised)
                .Push(ImGuiCol.ButtonHovered, RaisedHovered)
                .Push(ImGuiCol.ButtonActive, RaisedActive)
                .Push(ImGuiCol.Header, Raised)
                .Push(ImGuiCol.HeaderHovered, RaisedHovered)
                .Push(ImGuiCol.HeaderActive, RaisedActive)
                .Push(ImGuiCol.Tab, Rgb(26, 26, 26))
                .Push(ImGuiCol.TabHovered, RaisedHovered)
                .Push(ImGuiCol.TabActive, Rgb(60, 60, 60))
                .Push(ImGuiCol.TabUnfocused, Rgb(26, 26, 26))
                .Push(ImGuiCol.TabUnfocusedActive, Rgb(46, 46, 46))
                .Push(ImGuiCol.CheckMark, Accent)
                .Push(ImGuiCol.SliderGrab, Accent)
                .Push(ImGuiCol.SliderGrabActive, AccentActive)
                .Push(ImGuiCol.ScrollbarBg, Rgba(19, 19, 19, 0.5f))
                .Push(ImGuiCol.ScrollbarGrab, Rgb(52, 52, 52))
                .Push(ImGuiCol.ScrollbarGrabHovered, Rgb(70, 70, 70))
                .Push(ImGuiCol.ScrollbarGrabActive, Rgb(86, 86, 86))
                .Push(ImGuiCol.TextSelectedBg, WithAlpha(Accent, 0.35f))
                .Push(ImGuiCol.Text, Text)
                .Push(ImGuiCol.TextDisabled, TextFaint);

            _styles = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 8f)
                .Push(ImGuiStyleVar.ChildRounding, 6f)
                .Push(ImGuiStyleVar.FrameRounding, 5f)
                .Push(ImGuiStyleVar.PopupRounding, 6f)
                .Push(ImGuiStyleVar.GrabRounding, 4f)
                .Push(ImGuiStyleVar.TabRounding, 5f)
                .Push(ImGuiStyleVar.ScrollbarRounding, 6f)
                .Push(ImGuiStyleVar.ScrollbarSize, 10f)
                .Push(ImGuiStyleVar.WindowBorderSize, 1f)
                .Push(ImGuiStyleVar.WindowPadding, new Vector2(10, 9))
                .Push(ImGuiStyleVar.FramePadding, new Vector2(6, 4))
                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(7, 5));
        }

        public void Dispose()
        {
            _styles.Dispose();
            _colors.Dispose();
        }
    }

    private static Vector4 Rgb(byte red, byte green, byte blue)
    {
        return new Vector4(red / 255f, green / 255f, blue / 255f, 1f);
    }

    private static Vector4 Rgba(byte red, byte green, byte blue, float alpha)
    {
        return new Vector4(red / 255f, green / 255f, blue / 255f, alpha);
    }
}
