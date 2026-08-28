using Dalamud.Utility;
using Lumina.Excel.Exceptions;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using GimmickYesNo = Lumina.Excel.Sheets.GimmickYesNo;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Functions;

[RegisterSingleton]
internal sealed class ExcelFunctions(IDataManager dataManager, ILogger<ExcelFunctions> logger)
{
    private readonly IDataManager _dataManager = dataManager;
    private readonly ILogger<ExcelFunctions> _logger = logger;

    public StringOrRegex GetDialogueText(Quest? currentQuest, string? excelSheetName, string key, bool isRegex)
    {
        ReadOnlySeString? seString = GetRawDialogueText(currentQuest, excelSheetName, key);
        // Return empty string if dialogue text lookup fails to prevent ArgumentNullException
        if (seString == null)
        {
            _logger.LogWarning("Could not find dialogue text for key '{Key}' in sheet '{Sheet}'", key, excelSheetName);
            return new(string.Empty);
        }

        if (isRegex)
            return new(seString.Value.ToRegex());

        return new(seString.Value.WithCertainMacroCodeReplacements());
    }

    public ReadOnlySeString? GetRawDialogueText(Quest? currentQuest, string? excelSheetName, string key)
    {
        if (currentQuest != null && excelSheetName == null)
        {
            Lumina.Excel.Sheets.Quest? questRow =
                _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().GetRowOrDefault((uint)currentQuest.Id.Value + 0x10000);
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
            ExcelSheet<QuestDialogueText> excelSheet = _dataManager.GetExcelSheet<QuestDialogueText>(name: excelSheetName);
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
        ReadOnlySeString? seString = GetRawDialogueTextByRowId(excelSheet, rowId);
        if (isRegex)
            return new(seString?.ToRegex());

        return new(seString?.ToDalamudString().ToString());
    }

    public ReadOnlySeString? GetRawDialogueTextByRowId(string? excelSheet, uint rowId)
    {
        // Support raw sheets for dialogue text lookup by RowId
        // quest, custom, cut_scene, and dungeon sheets all use QuestDialogueText
        if (excelSheet?.StartsWith("quest/") == true || excelSheet?.StartsWith("custom/") == true ||
            excelSheet?.StartsWith("cut_scene/") == true || excelSheet?.StartsWith("dungeon/") == true)
        {
            try
            {
                ExcelSheet<QuestDialogueText>? dialogueSheet = _dataManager.GetExcelSheet<QuestDialogueText>(name: excelSheet);
                return dialogueSheet?.GetRowOrDefault(rowId)?.Value;
            }
            catch (SheetNotFoundException e)
            {
                _logger.LogError(e, "Could not find dialogue sheet '{Sheet}'", excelSheet);
                return null;
            }
        }

        if (string.Equals(excelSheet, "GimmickYesNo", StringComparison.Ordinal))
        {
            GimmickYesNo? questRow = _dataManager.GetExcelSheet<GimmickYesNo>().GetRowOrDefault(rowId);
            return questRow?.YesButton;
        }
        if (string.Equals(excelSheet, "Warp", StringComparison.Ordinal))
        {
            Warp? questRow = _dataManager.GetExcelSheet<Warp>().GetRowOrDefault(rowId);
            return questRow?.Name;
        }
        if (string.Equals(excelSheet, "Addon", StringComparison.Ordinal))
        {
            Addon? questRow = _dataManager.GetExcelSheet<Addon>().GetRowOrDefault(rowId);
            return questRow?.Text;
        }
        if (string.Equals(excelSheet, "EventPathMove", StringComparison.Ordinal))
        {
            EventPathMove? questRow = _dataManager.GetExcelSheet<EventPathMove>().GetRowOrDefault(rowId);
            return questRow?.Unknown0;
        }
        if (string.Equals(excelSheet, "GilShop", StringComparison.Ordinal))
        {
            GilShop? questRow = _dataManager.GetExcelSheet<GilShop>().GetRowOrDefault(rowId);
            return questRow?.Name;
        }
        if (string.Equals(excelSheet, "ContentTalk", StringComparison.Ordinal))
        {
            ContentTalk? questRow = _dataManager.GetExcelSheet<ContentTalk>().GetRowOrDefault(rowId);
            return questRow?.Text;
        }

        throw new ArgumentOutOfRangeException(nameof(excelSheet), $"Unsupported excel sheet {excelSheet}");
    }
}
