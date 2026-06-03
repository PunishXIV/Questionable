using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace Questionable.Utils;

internal static class ImGuiComponentsLocal
{
    internal static bool IconButton(string id, FontAwesomeIcon icon, Vector4? defaultColor = null,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        return IconButton(icon, null, null, null, null, file, line, id);
    }
    internal static bool IconButton(FontAwesomeIcon icon,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        return IconButton(icon, null, null, null, null, file, line);
    }
    internal static bool IconButton(FontAwesomeIcon icon, Vector2 size,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        return IconButton(icon, null, null, null, size, file, line);
    }
    internal static bool IconButton(FontAwesomeIcon icon,
                                    Vector4? defaultColor,
                                    Vector4? activeColor = null,
                                    Vector4? hoveredColor = null,
                                    Vector2? size = null,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0,
                                    string? id = null)
    {
        id ??= $"{Path.GetFileName(file)}:{line}";
        ImGui.PushID(id);
        bool result = ImGuiComponents.IconButton(icon, defaultColor, activeColor, hoveredColor, size);
        ImGui.PopID();
        return result;
    }

    internal static bool IconButtonWithText(FontAwesomeIcon icon,
                                            string text,
                                            Vector2 size,
                           [CallerFilePath] string file = "",
                         [CallerLineNumber] int line = 0)
    {
        return IconButtonWithText(icon, text, null, null, null, size, file, line);
    }

    internal static bool IconButtonWithText(FontAwesomeIcon icon,
                                            string text,
                                            Vector4? defaultColor = null,
                                            Vector4? activeColor = null,
                                            Vector4? hoveredColor = null,
                                            Vector2? size = null,
                           [CallerFilePath] string file = "",
                         [CallerLineNumber] int line = 0,
                                    string? id = null)
    {
        id ??= $"{Path.GetFileName(file)}:{line}";
        ImGui.PushID(id);
        bool result = ImGuiComponents.IconButtonWithText(icon, text, defaultColor, activeColor, hoveredColor, size);
        ImGui.PopID();
        return result;
    }
}
