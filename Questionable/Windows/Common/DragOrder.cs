namespace Questionable.Windows.Common;

// One handle per row: drag it and the row follows the mouse.
internal sealed class DragOrder<T> where T : notnull
{
    private readonly List<(T Key, float Top, float Bottom)> _rows = [];
    private int _frame = -1;
    private bool _dragging;
    private T? _dragged;

    public void Handle(T key, bool enabled = true)
    {
        if (_frame != ImGui.GetFrameCount())
        {
            _frame = ImGui.GetFrameCount();
            _rows.Clear();
        }

        bool active = _dragging && EqualityComparer<T>.Default.Equals(_dragged, key);
        using (ImRaii.Disabled(!enabled))
            ImGuiComponentsLocal.IconButton($"##Move{key}", FontAwesomeIcon.ArrowsUpDown,
                active ? ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.ButtonActive)) : null);
        if (!enabled)
            return;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_L("Drag to change the order"));
        if (!_dragging && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            (_dragging, _dragged) = (true, key);

        float pad = ImGui.GetStyle().ItemSpacing.Y / 2;
        _rows.Add((key, ImGui.GetItemRectMin().Y - pad, ImGui.GetItemRectMax().Y + pad));
    }

    // Call after the rows; true when the dragged row is over another row this frame.
    public bool End(out int from, out int to)
    {
        from = to = -1;
        if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            (_dragging, _dragged) = (false, default);
        else if (_dragging && _frame == ImGui.GetFrameCount())
        {
            from = _rows.FindIndex(x => EqualityComparer<T>.Default.Equals(x.Key, _dragged));
            float y = ImGui.GetMousePos().Y;
            to = _rows.FindIndex(x => y >= x.Top && y < x.Bottom);
        }

        return from >= 0 && to >= 0 && from != to;
    }

    public bool End(List<T> list)
    {
        if (!End(out int from, out int to))
            return false;

        T item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        return true;
    }
}
