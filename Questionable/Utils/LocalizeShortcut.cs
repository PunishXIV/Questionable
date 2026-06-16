using System.Diagnostics.CodeAnalysis;

namespace Questionable.Utils;

[SuppressMessage("Style", "IDE1006:Naming Styles")]
internal static class LocalizeShortcut
{
    // Holds the in-memory Configuration singleton so that _L/_LF don't have to
    // hit Svc.PluginInterface.GetPluginConfig() (which deserializes the config
    // from disk) on every single call. Previously this caused ~130 disk reads
    // per QuestWindow.Draw() frame.
    private static Configuration? _configuration;

    internal static void Initialize(Configuration configuration)
    {
        _configuration = configuration;
    }

    internal static string _L(I18N.DotNet.PlainString input)
    {
        var outp = I18N.DotNet.GlobalLocalizer.Localize(input);
        if (_configuration is { Advanced.Debug: true })
            return $"{{{outp}}}";
        return outp;
    }

    internal static string _LF(string input, params object[] args)
    {
        var outp = I18N.DotNet.GlobalLocalizer.LocalizeFormat(input, args);
        if (_configuration is { Advanced.Debug: true })
            return $"{{{outp}}}";
        return outp;
    }
}
