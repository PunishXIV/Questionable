using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Resolves a dialogue choice's prompt/answer reference (an Excel key, an Excel row id, or a raw
///     string) into a concrete <see cref="StringOrRegex"/>. Shared by the dialogue-choice and yes/no
///     interaction handlers.
/// </summary>
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

    /// <summary>
    ///     Returns whether <paramref name="actualAnswer"/> matches <paramref name="expectedAnswer"/>,
    ///     tolerating embedded newlines in the actual answer.
    /// </summary>
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
