using Dalamud.Plugin.Services;
using LLib;
using Lumina.Excel;
using Lumina.Text.ReadOnly;
using System;
using System.Text.RegularExpressions;
namespace Questionable.Data;

internal static class DataManagerAdapter
{
    // Intentionally retained temporary bridge: keeps call sites LLib-agnostic until adapter internals are swapped.
    public static string? GetString<T>(IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> textSelector)
    where T : struct, IExcelRow<T>
    {
        return dataManager.GetString(rowId, textSelector);
    }

    public static Regex? GetRegex<T>(IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> textSelector,
        IPluginLog? pluginLog = null)
    where T : struct, IExcelRow<T>
    {
        return dataManager.GetRegex(rowId, textSelector, pluginLog);
    }
}
