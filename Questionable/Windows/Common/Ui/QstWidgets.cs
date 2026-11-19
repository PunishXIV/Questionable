using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Questionable.Windows.Common.Ui;

internal static class QstWidgets
{
    // Collapsible section header with an optional count.
    public static bool SectionHeader(string label, string id, int? count = null, bool defaultOpen = true)
    {
        ImGuiTreeNodeFlags flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        using ImRaii.ColorDisposable headerColor =
            ImRaii.PushColor(ImGuiCol.Header, QstTheme.WithAlpha(QstTheme.Text, 0f));
        using ImRaii.ColorDisposable hoverColor =
            ImRaii.PushColor(ImGuiCol.HeaderHovered, QstTheme.WithAlpha(QstTheme.Text, 0.06f));
        using ImRaii.ColorDisposable activeColor =
            ImRaii.PushColor(ImGuiCol.HeaderActive, QstTheme.WithAlpha(QstTheme.Text, 0.09f));
        using ImRaii.ColorDisposable textColor = ImRaii.PushColor(ImGuiCol.Text, QstTheme.TextMuted);

        string headerLabel = label.ToUpper(CultureInfo.CurrentUICulture);
        bool open = ImGui.CollapsingHeader($"{headerLabel}###{id}", flags);

        if (count != null)
        {
            string countText = count.Value.ToString(CultureInfo.CurrentCulture);
            Vector2 textSize = ImGui.CalcTextSize(countText);
            Vector2 min = ImGui.GetItemRectMin();
            Vector2 max = ImGui.GetItemRectMax();
            float labelEnd = ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.X * 3
                             + ImGui.CalcTextSize(headerLabel).X;
            Vector2 pos = new(
                min.X + labelEnd + ImGui.GetStyle().ItemInnerSpacing.X * 2,
                min.Y + (max.Y - min.Y - textSize.Y) / 2f);
            ImGui.GetWindowDrawList().AddText(pos, QstTheme.ToU32(QstTheme.TextMuted), countText);
        }

        return open;
    }

    public static void BulletTextWrapped(string text)
    {
        ImGui.Bullet();
        ImGui.TextWrapped(text);
    }

    // Status pill in the window title bar.
    public static void TitleBarPill(string text, Vector4 color, string windowTitle, bool alignCenter = true)
    {
        float frameHeight = ImGui.GetFrameHeight();
        Vector2 windowPos = ImGui.GetWindowPos();
        float windowWidth = ImGui.GetWindowWidth();
        float scale = ImGuiHelpers.GlobalScale;

        Vector2 textSize = ImGui.CalcTextSize(text);
        float dotRadius = 3f * scale;
        Vector2 padding = new(7f * scale, 1.5f * scale);
        float height = Math.Min(textSize.Y + padding.Y * 2f, frameHeight - 4f * scale);
        float width = padding.X * 2f + dotRadius * 2f + 4f * scale + textSize.X;

        string visibleTitle = windowTitle.Split("###")[0];
        float titleStart = ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.X;
        //+ ImGui.GetStyle().ItemInnerSpacing.X;
        Vector2 topLeft = new(
            windowPos.X + titleStart + (alignCenter ? ImGui.CalcTextSize(visibleTitle).X - 3f * scale : 0f),
            windowPos.Y + (frameHeight - height) / 2f);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(windowPos, windowPos + new Vector2(windowWidth, frameHeight), intersectWithCurrentClipRect: false);
        drawList.AddRectFilled(topLeft, topLeft + new Vector2(width, height),
            QstTheme.ToU32(QstTheme.WithAlpha(color, 0.15f)), height / 2f);
        drawList.AddCircleFilled(new Vector2(topLeft.X + padding.X + dotRadius, topLeft.Y + height / 2f),
            dotRadius, QstTheme.ToU32(color));
        drawList.AddText(new Vector2(topLeft.X + padding.X + dotRadius * 2f + 4f * scale,
            topLeft.Y + (height - textSize.Y) / 2f), QstTheme.ToU32(color), text);
        drawList.PopClipRect();
    }

    // Inline status pill.
    public static void StatusPill(string text, Vector4 color)
    {
        float scale = ImGuiHelpers.GlobalScale;
        Vector2 textSize = ImGui.CalcTextSize(text);
        float dotRadius = 3f * scale;
        Vector2 padding = new(8f * scale, 2f * scale);
        float height = textSize.Y + padding.Y * 2;
        float width = padding.X * 2 + dotRadius * 2 + 4f * scale + textSize.X;

        Vector2 pos = ImGui.GetCursorScreenPos();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(width, height),
            QstTheme.ToU32(QstTheme.WithAlpha(color, 0.15f)), height / 2f);
        drawList.AddCircleFilled(new Vector2(pos.X + padding.X + dotRadius, pos.Y + height / 2f),
            dotRadius, QstTheme.ToU32(color));
        drawList.AddText(new Vector2(pos.X + padding.X + dotRadius * 2 + 4f * scale, pos.Y + padding.Y),
            QstTheme.ToU32(color), text);
        ImGui.Dummy(new Vector2(width, height));
    }

    // Slim progress bar.
    public static void ThinProgressBar(float fraction, Vector4 color, float height = 4f)
    {
        float barHeight = height * ImGuiHelpers.GlobalScale;
        float width = ImGui.GetContentRegionAvail().X;
        float rounding = barHeight / 2f;

        Vector2 pos = ImGui.GetCursorScreenPos();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(width, barHeight),
            QstTheme.ToU32(QstTheme.WithAlpha(color, 0.2f)), rounding);

        float clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped > 0f)
            drawList.AddRectFilled(pos, pos + new Vector2(width * clamped, barHeight),
                QstTheme.ToU32(color), rounding);

        ImGui.Dummy(new Vector2(0, barHeight));
    }

    // Small rounded label.
    public static bool Chip(string text, Vector4 color)
    {
        float scale = ImGuiHelpers.GlobalScale;
        Vector2 textSize = ImGui.CalcTextSize(text);
        Vector2 padding = new(6f * scale, 1f * scale);
        Vector2 size = textSize + padding * 2;

        Vector2 pos = ImGui.GetCursorScreenPos();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + size, QstTheme.ToU32(QstTheme.WithAlpha(color, 0.13f)), size.Y / 2f);
        drawList.AddText(pos + padding, QstTheme.ToU32(color), text);
        ImGui.Dummy(size);
        return ImGui.IsItemHovered();
    }

    // Icon button with a tooltip and an optional count badge.
    public static bool RailButton(FontAwesomeIcon icon, string label, string? tooltip = null, Vector4? tint = null,
        bool enabled = true, string? countBadge = null, Vector4? badgeColor = null, bool showLabel = false,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        float size = ImGui.GetFrameHeight();
        bool clicked;
        using (ImRaii.Disabled(!enabled))
        {
            if (showLabel)
                clicked = ImGuiComponentsLocal.IconButtonWithText(icon, label, tint, activeColor: null, hoveredColor: null,
                    file: file, line: line);
            else
                clicked = ImGuiComponentsLocal.IconButton(icon, tint, activeColor: null, hoveredColor: null,
                    new Vector2(size, size), file, line);
        }

        if (!showLabel && countBadge != null)
        {
            Vector2 max = ImGui.GetItemRectMax();
            Vector2 badgeSize = ImGui.CalcTextSize(countBadge);
            Vector2 pos = new(max.X - badgeSize.X - 1f, max.Y - badgeSize.Y + 2f);
            ImGui.GetWindowDrawList().AddText(pos, QstTheme.ToU32(badgeColor ?? QstTheme.TextMuted), countBadge);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (showLabel && tooltip?.Length != 0)
                ImGui.SetTooltip(tooltip);
            else
            {
                if (tooltip?.Length != 0)
                    tooltip = $"\n{tooltip}";
                ImGui.SetTooltip($"{label}{tooltip}");
            }
        }

        return clicked && enabled;
    }

    // Boxed group of SegmentToggles.
    public static SegmentGroupScope SegmentGroup(bool armed) => new(armed);

    // Inset content box.
    public static CardScope Card(Vector4? borderColor = null) => new(borderColor);

    internal sealed class CardScope : IDisposable
    {
        private readonly Vector2 _topLeft;
        private readonly float _width;
        private readonly float _padding;
        private readonly Vector4 _borderColor;

        internal CardScope(Vector4? borderColor)
        {
            _padding = 7f * ImGuiHelpers.GlobalScale;
            _topLeft = ImGui.GetCursorScreenPos();
            _width = ImGui.GetContentRegionAvail().X;
            _borderColor = borderColor ?? QstTheme.Edge;

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            drawList.ChannelsSplit(2);
            drawList.ChannelsSetCurrent(1);
            ImGui.SetCursorScreenPos(_topLeft + new Vector2(_padding, _padding));
            ImGui.BeginGroup();
        }

        public void Dispose()
        {
            ImGui.EndGroup();
            Vector2 contentMax = ImGui.GetItemRectMax();
            Vector2 bottomRight = new(_topLeft.X + _width, contentMax.Y + _padding);

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            drawList.ChannelsSetCurrent(0);
            float rounding = 6f * ImGuiHelpers.GlobalScale;
            drawList.AddRectFilled(_topLeft, bottomRight, QstTheme.ToU32(QstTheme.InputBg), rounding);
            drawList.AddRect(_topLeft, bottomRight, QstTheme.ToU32(_borderColor), rounding);
            drawList.ChannelsMerge();

            ImGui.SetCursorScreenPos(new Vector2(_topLeft.X, bottomRight.Y));
            ImGui.Dummy(new Vector2(0, 0));
        }
    }

    // Toggle button for a SegmentGroup.
    public static bool SegmentToggle(FontAwesomeIcon icon, bool active, string tooltipOn, string tooltipOff,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        float size = ImGui.GetFrameHeight();
        Vector4? tint = active ? QstTheme.Accent : null;
        bool clicked = ImGuiComponentsLocal.IconButton(icon, tint, activeColor: null, hoveredColor: null,
            new Vector2(size, size), file, line);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using ImRaii.TooltipDisposable _ = ImRaii.Tooltip();
            ImGui.TextUnformatted(active ? tooltipOn : tooltipOff);
        }

        return clicked;
    }

    internal sealed class SegmentGroupScope : IDisposable
    {
        private readonly ImRaii.StyleDisposable _spacing;
        private readonly ImRaii.ColorDisposable? _armedTint;
        private readonly bool _armed;

        internal SegmentGroupScope(bool armed)
        {
            _armed = armed;
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            drawList.ChannelsSplit(2);
            drawList.ChannelsSetCurrent(1);

            _spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing,
                new Vector2(1f * ImGuiHelpers.GlobalScale, ImGui.GetStyle().ItemSpacing.Y));
            if (armed)
                _armedTint = ImRaii.PushColor(ImGuiCol.Button, QstTheme.WithAlpha(QstTheme.Accent, 0.15f));
            ImGui.BeginGroup();
        }

        public void Dispose()
        {
            ImGui.EndGroup();
            _armedTint?.Dispose();
            _spacing.Dispose();

            float inset = 2f * ImGuiHelpers.GlobalScale;
            Vector2 min = ImGui.GetItemRectMin() - new Vector2(inset, inset);
            Vector2 max = ImGui.GetItemRectMax() + new Vector2(inset, inset);

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            drawList.ChannelsSetCurrent(0);
            float rounding = 6f * ImGuiHelpers.GlobalScale;
            drawList.AddRectFilled(min, max, QstTheme.ToU32(QstTheme.InputBg), rounding);
            drawList.AddRect(min, max,
                QstTheme.ToU32(_armed ? QstTheme.WithAlpha(QstTheme.Accent, 0.55f) : QstTheme.Edge), rounding);
            drawList.ChannelsMerge();
        }
    }
}
