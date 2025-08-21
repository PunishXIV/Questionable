using System;
using System.Linq;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using LLib;
using Lumina.Excel.Exceptions;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Microsoft.Extensions.Logging;
using Questionable.Model;
using Quest = Questionable.Model.Quest;
using GimmickYesNo = Lumina.Excel.Sheets.GimmickYesNo;

namespace Questionable.Functions;

internal sealed class ExcelFunctions
{
    private readonly IDataManager _dataManager;
    private readonly ILogger<ExcelFunctions> _logger;

    public ExcelFunctions(IDataManager dataManager, ILogger<ExcelFunctions> logger)
    {
        _dataManager = dataManager;
        _logger = logger;
    }

    public StringOrRegex GetDialogueText(Quest? currentQuest, string? excelSheetName, string key, bool isRegex)
    {
        var seString = GetRawDialogueText(currentQuest, excelSheetName, key);
        if (isRegex)
            return new StringOrRegex(seString.ToRegex());
        else
            return new StringOrRegex(seString?.WithCertainMacroCodeReplacements());
    }

    public ReadOnlySeString? GetRawDialogueText(Quest? currentQuest, string? excelSheetName, string key)
    {
        if (currentQuest != null && excelSheetName == null)
        {
            var questRow =
                _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().GetRowOrDefault((uint)currentQuest.Id.Value +
                    0x10000);
            if (questRow == null)
            {
                _logger.LogError("Could not find quest row for {QuestId}", currentQuest.Id);
                return null;
            }

            excelSheetName = $"quest/{(currentQuest.Id.Value / 100):000}/{questRow.Value.Id}";
        }

        ArgumentNullException.ThrowIfNull(excelSheetName);
        try
        {
            var excelSheet = _dataManager.GetExcelSheet<QuestDialogueText>(name: excelSheetName);
            return excelSheet.Cast<QuestDialogueText?>()
                .FirstOrDefault(x => x!.Value.Key == key)?.Value;
        }
        catch (SheetNotFoundException e)
        {
            throw new SheetNotFoundException($"Sheet '{excelSheetName}' not found", e);
        }
    }

    public StringOrRegex GetDialogueTextByRowId(string? excelSheet, uint rowId, bool isRegex)
    {
        var seString = GetRawDialogueTextByRowId(excelSheet, rowId);
        if (isRegex)
            return new StringOrRegex(seString.ToRegex());
        else
            return new StringOrRegex(seString?.ToDalamudString().ToString());
    }

    public ReadOnlySeString? GetRawDialogueTextByRowId(string? excelSheet, uint rowId)
    {
        if (excelSheet == "GimmickYesNo")
        {
            var questRow = _dataManager.GetExcelSheet<GimmickYesNo>().GetRowOrDefault(rowId);
            return questRow?.Unknown0;
        }
        else if (excelSheet == "Warp")
        {
            var questRow = _dataManager.GetExcelSheet<Warp>().GetRowOrDefault(rowId);
            return questRow?.Name;
        }
        else if (excelSheet is "Addon")
        {
            var questRow = _dataManager.GetExcelSheet<Addon>().GetRowOrDefault(rowId);
            return questRow?.Text;
        }
        else if (excelSheet is "EventPathMove")
        {
            var questRow = _dataManager.GetExcelSheet<EventPathMove>().GetRowOrDefault(rowId);
            return questRow?.Unknown0;
        }
        else if (excelSheet is "GilShop")
        {
            var questRow = _dataManager.GetExcelSheet<GilShop>().GetRowOrDefault(rowId);
            return questRow?.Name;
        }
        else if (excelSheet is "ContentTalk" or null)
        {
            var questRow = _dataManager.GetExcelSheet<ContentTalk>().GetRowOrDefault(rowId);
            return questRow?.Text;
        }
        else
            throw new ArgumentOutOfRangeException(nameof(excelSheet), $"Unsupported excel sheet {excelSheet}");
    }
}
