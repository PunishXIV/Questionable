using System.Diagnostics.CodeAnalysis;

namespace Questionable.Utils;

[SuppressMessage("Style", "IDE1006:Naming Styles")]
internal static class LocalizeShortcut
{
    private static bool _debug;

    internal static void Initialize(Configuration configuration)
    {
        _debug = configuration.Advanced.Debug;
    }

    internal static string _L(I18N.DotNet.PlainString input)
    {
        var outp = I18N.DotNet.GlobalLocalizer.Localize(input);
        if (_debug)
            return $"{{{outp}}}";
        return outp;
    }

    internal static string _LF(string input, params object[] args)
    {
        var outp = I18N.DotNet.GlobalLocalizer.LocalizeFormat(input, args);
        if (_debug)
            return $"{{{outp}}}";
        return outp;
    }
}
