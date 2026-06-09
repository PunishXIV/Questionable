using System.Diagnostics.CodeAnalysis;

namespace Questionable.Utils;

[SuppressMessage("Style", "IDE1006:Naming Styles")]
internal static class LocalizeShortcut
{
    internal static string _L(I18N.DotNet.PlainString input) => I18N.DotNet.GlobalLocalizer.Localize(input);
    internal static string _LF(string input, params object[] args) => I18N.DotNet.GlobalLocalizer.LocalizeFormat(input, args);
}
