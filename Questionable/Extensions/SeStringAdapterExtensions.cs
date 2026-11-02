using Dalamud.Game.Text.SeStringHandling;
using Lumina.Text.ReadOnly;

namespace Questionable.Extensions;

internal static class SeStringAdapterExtensions
{
    public static string WithCertainMacroCodeReplacements(this SeString? str)
    {
        if (str == null)
            return string.Empty;

        ReadOnlySeString seString = new(str.Encode());
        return seString.WithCertainMacroCodeReplacements();
    }
}
