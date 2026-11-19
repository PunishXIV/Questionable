using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

[RegisterSingleton<IQuestValidator, DialogueChoiceValidator>(Duplicate = DuplicateStrategy.Append)]
internal sealed class DialogueChoiceValidator(ExcelFunctions excelFunctions) : IQuestValidator
{
    private readonly ExcelFunctions _excelFunctions = excelFunctions;

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        foreach ((QuestSequence Sequence, int StepId, QuestStep Step) in quest.AllSteps())
        {
            if (Step.DialogueChoices.Count == 0)
                continue;

            foreach (DialogueChoice dialogueChoice in Step.DialogueChoices)
            {
                ExcelRef? prompt = dialogueChoice.Prompt;
                if (prompt != null)
                {
                    ValidationIssue? promptIssue = Validate(quest, Sequence, StepId, dialogueChoice.ExcelSheet,
                        prompt, "Prompt");
                    if (promptIssue != null)
                        yield return promptIssue;
                }

                ExcelRef? answer = dialogueChoice.Answer;
                if (answer != null)
                {
                    ValidationIssue? answerIssue = Validate(quest, Sequence, StepId, dialogueChoice.ExcelSheet,
                        answer, "Answer");
                    if (answerIssue != null)
                        yield return answerIssue;
                }
            }
        }
    }

    private ValidationIssue? Validate(Quest quest, QuestSequence sequence, int stepId, string? excelSheet,
        ExcelRef excelRef, string label)
    {
        if (excelRef.Type == ExcelRef.EType.Key)
        {
            if (_excelFunctions.GetRawDialogueText(quest, excelSheet, excelRef.AsKey()) == null)
            {
                return new()
                {
                    ElementId = quest.Id,
                    Sequence = sequence.Sequence,
                    Step = stepId,
                    Type = EIssueType.InvalidExcelRef,
                    Severity = EIssueSeverity.Error,
                    Description = _LF("{0} invalid: {1} → {2}", label, excelSheet?.ToString() ?? "", excelRef.AsKey())
                };
            }
        }
        else if (excelRef.Type == ExcelRef.EType.RowId)
        {
            if (_excelFunctions.GetRawDialogueTextByRowId(excelSheet, excelRef.AsRowId()) == null)
            {
                return new()
                {
                    ElementId = quest.Id,
                    Sequence = sequence.Sequence,
                    Step = stepId,
                    Type = EIssueType.InvalidExcelRef,
                    Severity = EIssueSeverity.Error,
                    Description = _LF("{0} invalid: {1} → {2}", label, excelSheet?.ToString() ?? "", excelRef.AsKey())
                };
            }
        }

        return null;
    }
}
