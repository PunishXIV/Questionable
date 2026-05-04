using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
namespace Questionable.Data;

public static class DataManagerExtensions
{
    public static ReadOnlySeString? GetSeString<T>(this IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> mapper)
    where T : struct, IExcelRow<T>
    {
        ArgumentNullException.ThrowIfNull(dataManager);
        ArgumentNullException.ThrowIfNull(mapper);

        T? row = dataManager.GetExcelSheet<T>().GetRowOrDefault(rowId);
        if (row == null)
            return null;

        return mapper(row.Value);
    }

    public static string? GetString<T>(this IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> mapper,
        IPluginLog? pluginLog = null)
    where T : struct, IExcelRow<T>
    {
        string? text = dataManager.GetSeString(rowId, mapper)?.WithCertainMacroCodeReplacements();

        pluginLog?.Verbose($"{typeof(T).Name}.{rowId} => {text}");
        return text;
    }

    public static Regex? GetRegex<T>(this IDataManager dataManager, uint rowId, Func<T, ReadOnlySeString?> mapper,
        IPluginLog? pluginLog = null)
    where T : struct, IExcelRow<T>
    {
        ReadOnlySeString? text = dataManager.GetSeString(rowId, mapper);
        if (text == null)
            return null;

        Regex regex = text.ToRegex();
        pluginLog?.Verbose($"{typeof(T).Name}.{rowId} => /{regex}/");
        return regex;
    }

    public static Regex ToRegex(this ReadOnlySeString? text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(string.Join("", text.Value.Select(payload =>
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
                    var _ => payload.ToString()
                },
                var _ => payload.ToString()
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
[Sheet("QuestDialogueText")]
public readonly struct QuestDialogueText(ExcelPage page, uint offset, uint row) : IQuestDialogueText, IExcelRow<QuestDialogueText>
{
    public uint RowId => row;

    public ReadOnlySeString Key => page.ReadString(offset, offset);
    public ReadOnlySeString Value => page.ReadString(offset + 4, offset);

    ExcelPage IExcelRow<QuestDialogueText>.ExcelPage => page;

    uint IExcelRow<QuestDialogueText>.RowOffset => offset;

    static QuestDialogueText IExcelRow<QuestDialogueText>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
}
