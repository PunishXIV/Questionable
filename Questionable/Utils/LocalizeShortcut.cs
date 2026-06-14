using System.Diagnostics.CodeAnalysis;
using ECommons.DalamudServices;

namespace Questionable.Utils;

[SuppressMessage("Style", "IDE1006:Naming Styles")]
internal static class LocalizeShortcut
{
    internal static string _L(I18N.DotNet.PlainString input)
    {
        var outp = I18N.DotNet.GlobalLocalizer.Localize(input);
        if ((Configuration?)Svc.PluginInterface.GetPluginConfig() is { } config && config.Advanced.Debug)
            return $"{{{outp}}}";
        return outp;
    }
    internal static string _LF(string input, params object[] args)
    {
        var outp = I18N.DotNet.GlobalLocalizer.LocalizeFormat(input, args);
        if ((Configuration?)Svc.PluginInterface.GetPluginConfig() is { } config && config.Advanced.Debug)
            return $"{{{outp}}}";
        return outp;
    }
}
