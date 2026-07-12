using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using Questionable.Extensions;
namespace Questionable.Utils;

internal static class AtkValueAdapter
{
    public static unsafe string? ReadString(AtkValue value)
    {
        if (value.Type == AtkValueType.Undefined)
            return null;

        if (value.String.HasValue)
            return MemoryHelper.ReadSeStringNullTerminated(new(value.String)).WithCertainMacroCodeReplacements();

        return null;
    }
}
