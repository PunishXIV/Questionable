using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace Questionable.Extensions;

internal static class SeStringAdapterExtensions
{
    public static string WithCertainMacroCodeReplacements(this SeString? str)
    {
        if (str == null)
            return string.Empty;

        ReadOnlySeString seString = new(str.Encode());
        return seString.WithCertainMacroCodeReplacementsFromReadOnly();
    }

    public static string WithCertainMacroCodeReplacementsFromReadOnly(this ReadOnlySeString text)
    {
        return string.Join("", text.Select(payload =>
        {
            return payload.Type switch
            {
                ReadOnlySePayloadType.Text => payload.ToString(),
                ReadOnlySePayloadType.Macro => payload.MacroCode switch
                {
                    MacroCode.NewLine => "",
                    MacroCode.NonBreakingSpace => " ",
                    MacroCode.Hyphen => "-",
                    MacroCode.SoftHyphen => "",
                    var _ => payload.ToString()
                },
                var _ => payload.ToString()
            };
        }));
    }
}
