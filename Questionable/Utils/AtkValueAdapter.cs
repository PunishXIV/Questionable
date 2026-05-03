using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
namespace Questionable.Utils;

internal static class AtkValueAdapter
{
    public static string? ReadString(AtkValue value)
    {
        return value.ReadAtkString();
    }
}
