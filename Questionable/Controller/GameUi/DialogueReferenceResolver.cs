using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class DialogueReferenceResolver(ExcelFunctions excelFunctions)
{
    public StringOrRegex? Resolve(Quest? quest, string? excelSheet, ExcelRef? excelRef, bool isRegExp)
    {
        if (excelRef == null)
            return null;

        if (excelRef.Type == ExcelRef.EType.Key)
            return excelFunctions.GetDialogueText(quest, excelSheet, excelRef.AsKey(), isRegExp);
        if (excelRef.Type == ExcelRef.EType.RowId)
            return excelFunctions.GetDialogueTextByRowId(excelSheet, excelRef.AsRowId(), isRegExp);

        if (excelRef.Type == ExcelRef.EType.RawString)
            return new(excelRef.AsRawString());

        return null;
    }

    // FFXIV dialogue answers can contain embedded newlines.
    public static bool IsMatch(string? actualAnswer, StringOrRegex? expectedAnswer)
    {
        if (actualAnswer == null && expectedAnswer == null)
            return true;

        if (actualAnswer == null || expectedAnswer == null)
            return false;

        return expectedAnswer.IsMatch(actualAnswer) ||
               expectedAnswer.IsMatch(actualAnswer.Replace("\n", string.Empty)) ||
               expectedAnswer.IsMatch(actualAnswer.Split('\n')[0]);
    }
}
