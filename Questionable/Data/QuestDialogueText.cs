using System.Diagnostics.CodeAnalysis;
using Lumina.Text.ReadOnly;
namespace Questionable.Data;

[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Unused operations")]
[Sheet("QuestDialogueText")]
public readonly struct QuestDialogueText(ExcelPage page, uint offset, uint row) : IExcelRow<QuestDialogueText>
{
    public uint RowId => row;

    public ReadOnlySeString Key => page.ReadString(offset, offset);
    public ReadOnlySeString Value => page.ReadString(offset + 4, offset);

    ExcelPage IExcelRow<QuestDialogueText>.ExcelPage => page;

    uint IExcelRow<QuestDialogueText>.RowOffset => offset;

    static QuestDialogueText IExcelRow<QuestDialogueText>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);

}
