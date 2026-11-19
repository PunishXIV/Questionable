using System.Text.RegularExpressions;
using Lumina.Text.ReadOnly;
namespace Questionable.Data;

internal static class DataManagerAdapter
{
    public static string? GetString<T>(IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> textSelector)
    where T : struct, IExcelRow<T> => dataManager.GetString(rowId, textSelector);

    public static Regex? GetRegex<T>(IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> textSelector,
        IPluginLog? pluginLog = null)
    where T : struct, IExcelRow<T> => dataManager.GetRegex(rowId, textSelector, pluginLog);
}
