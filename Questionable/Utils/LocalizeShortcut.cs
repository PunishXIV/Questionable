using System.Diagnostics.CodeAnalysis;
using Lumina.Excel.Sheets;

namespace Questionable.Utils;

[SuppressMessage("Style", "IDE1006:Naming Styles")]
internal static class LocalizeShortcut
{
    private static bool _debug;
    private static IDataManager _dataManager;
    private static IClientState _clientState;

    internal static void Initialize(Configuration configuration, IDataManager dataManager, IClientState clientState)
    {
        _debug = configuration.Advanced.Debug && configuration.Advanced.DebugLocalisation;
        _dataManager = dataManager;
        _clientState = clientState;
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

    private static readonly Dictionary<(Type, uint), string> _translatedStrings = [];
    public static string _T<T>(uint rowId) where T : struct, IExcelRow<T>
    {
        if (_translatedStrings.TryGetValue((typeof(T), rowId), out var match))
            return match;

        string value = _dataManager.GetExcelSheet<T>(_clientState.ClientLanguage).GetRow(rowId) switch
        {
            JournalGenre journalGenre => journalGenre.Name.ToMacroString(),
            JournalCategory journalCategory => journalCategory.Name.ToMacroString(),
            Addon addon => addon.Text.ToMacroString(),
            ExVersion exVersion => exVersion.Name.ToMacroString(),
            BannerBg bannerBg => bannerBg.Name.ToMacroString(),
            BeastTribe beastTribe => beastTribe.Name.ToMacroString(),
            AchievementCategory achievementCategory => achievementCategory.Name.ToMacroString(),
            CabinetSubCategory cabinetSubCategory => cabinetSubCategory.Name.ToMacroString(),
            _ => throw new InvalidOperationException($"No known Name/Text mapping for {typeof(T).Name}")
        };
        _translatedStrings[(typeof(T), rowId)] = value;
        return value;
    }
}
