using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;
namespace LLib.GameUI;

public static class LAtkValue
{
    public static unsafe string? ReadAtkString(this AtkValue atkValue)
    {
        if (atkValue.Type == AtkValueType.Undefined)
        {
            return null;
        }
        if (atkValue.String.HasValue)
        {
            return MemoryHelper.ReadSeStringNullTerminated(new(atkValue.String)).WithCertainMacroCodeReplacements();
        }
        return null;
    }
}

public static class SeStringExtensions
{
    public static string WithCertainMacroCodeReplacements(this SeString? str)
    {
        if (str == null)
        {
            return string.Empty;
        }

        ReadOnlySeString seString = new(str.Encode());
        return seString.WithCertainMacroCodeReplacements();
    }
}
