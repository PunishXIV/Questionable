using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace LLib;

public static class DataManagerExtensions
{
    public static ReadOnlySeString? GetSeString<T>(this IDataManager dataManager, string key)
        where T : struct, IQuestDialogueText, IExcelRow<T>
    {
        ArgumentNullException.ThrowIfNull(dataManager);

        return dataManager.GetExcelSheet<T>()
            .Cast<T?>()
            .SingleOrDefault(x => x!.Value.Key == key)
            ?.Value;
    }

    public static string? GetString<T>(this IDataManager dataManager, string key, IPluginLog? pluginLog)
        where T : struct, IQuestDialogueText, IExcelRow<T>
    {
        string? text = GetSeString<T>(dataManager, key)?.WithCertainMacroCodeReplacements();

        pluginLog?.Verbose($"{typeof(T).Name}.{key} => {text}");
        return text;
    }

    public static Regex? GetRegex<T>(this IDataManager dataManager, string key, IPluginLog? pluginLog)
        where T : struct, IQuestDialogueText, IExcelRow<T>
    {
        ReadOnlySeString? text = GetSeString<T>(dataManager, key);
        if (text == null)
            return null;

        string regex = string.Join("", text.Select((ReadOnlySePayload payload) =>
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
                return Regex.Escape(payload.ToString());
            else
                return "(.*)";
        }));
        pluginLog?.Verbose($"{typeof(T).Name}.{key} => /{regex}/");
        return new Regex(regex);
    }

    public static ReadOnlySeString? GetSeString<T>(this IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> mapper)
        where T : struct, IExcelRow<T>
    {
        ArgumentNullException.ThrowIfNull(dataManager);
        ArgumentNullException.ThrowIfNull(mapper);

        var row = dataManager.GetExcelSheet<T>().GetRowOrDefault(rowId);
        if (row == null)
            return null;

        return mapper(row.Value);
    }

    public static string? GetString<T>(this IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> mapper,
        IPluginLog? pluginLog = null)
        where T : struct, IExcelRow<T>
    {
        string? text = GetSeString(dataManager, rowId, mapper)?.WithCertainMacroCodeReplacements();

        pluginLog?.Verbose($"{typeof(T).Name}.{rowId} => {text}");
        return text;
    }

    public static Regex? GetRegex<T>(this IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> mapper,
        IPluginLog? pluginLog = null)
        where T : struct, IExcelRow<T>
    {
        ReadOnlySeString? text = GetSeString(dataManager, rowId, mapper);
        if (text == null)
            return null;

        Regex regex = text.ToRegex();
        pluginLog?.Verbose($"{typeof(T).Name}.{rowId} => /{regex}/");
        return regex;
    }

    public static Regex? GetRegex<T>(this T excelRow, Func<T, ReadOnlySeString?> mapper, IPluginLog? pluginLog)
        where T : struct, IExcelRow<T>
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ReadOnlySeString? text = mapper(excelRow);
        if (text == null)
            return null;

        Regex regex = text.ToRegex();
        pluginLog?.Verbose($"{typeof(T).Name}.regex => /{regex}/");
        return regex;
    }

    public static Regex ToRegex(this ReadOnlySeString? text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Regex(string.Join("", text.Value.Select(payload =>
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
                return Regex.Escape(payload.ToString());
            else
                return "(.*)";
        })));
    }

    public static string WithCertainMacroCodeReplacements(this ReadOnlySeString text)
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
                    _ => payload.ToString()
                },
                _ => payload.ToString()
            };
        }));
    }
}

public interface IQuestDialogueText
{
    public ReadOnlySeString Key { get; }
    public ReadOnlySeString Value { get; }
}

[SuppressMessage("Performance", "CA1815")]
[Sheet("PleaseSpecifyTheSheetExplicitly")]
public readonly struct QuestDialogueText(ExcelPage page, uint offset, uint row) : IQuestDialogueText, IExcelRow<QuestDialogueText>
{
    public uint RowId => row;

    public ReadOnlySeString Key => page.ReadString(offset, offset);
    public ReadOnlySeString Value => page.ReadString(offset + 4, offset);

    static QuestDialogueText IExcelRow<QuestDialogueText>.Create(ExcelPage page, uint offset, uint row) =>
        new(page, offset, row);
}
