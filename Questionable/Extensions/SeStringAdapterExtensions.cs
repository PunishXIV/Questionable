using Dalamud.Game.Text.SeStringHandling;
using Lumina.Text.ReadOnly;

namespace Questionable.Extensions;

internal static class SeStringAdapterExtensions
{
    public static string WithMacrosReplaced(this SeString? text)
    {
        if (text == null)
            return string.Empty;

        ReadOnlySeString seString = new(text.Encode());
        return seString.WithMacrosReplaced();
    }
}
