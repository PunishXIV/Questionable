using System.Collections.Generic;
using System.Linq;
using Lumina.Text.ReadOnly;
using Questionable.Functions;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Validation.Validators;

internal sealed class SayValidator : IQuestValidator
{
    private readonly ExcelFunctions _excelFunctions;

    public SayValidator(ExcelFunctions excelFunctions)
    {
        _excelFunctions = excelFunctions;
    }

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        foreach (var data in quest.AllSteps().Where(x => x.Step.InteractionType == EInteractionType.Say))
        {
            var chatMessage = data.Step.ChatMessage;
            if (chatMessage == null)
                continue;

            ReadOnlySeString? excelString = _excelFunctions
                .GetRawDialogueText(quest, chatMessage.ExcelSheet, chatMessage.Key);
            if (excelString == null)
                continue;

            if (excelString.Value.PayloadCount != 1)
            {
                yield return new ValidationIssue
                {
                    ElementId = quest.Id,
                    Sequence = data.Sequence.Sequence,
                    Step = data.StepId,
                    Type = EIssueType.InvalidChatMessage,
                    Severity = EIssueSeverity.Error,
                    Description = $"Invalid chat message: {excelString.Value}",
                };
            }
        }
    }
}
