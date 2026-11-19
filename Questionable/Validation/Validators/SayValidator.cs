using Lumina.Text.ReadOnly;
using Questionable.Model.Questing;
namespace Questionable.Validation.Validators;

[RegisterSingleton<IQuestValidator, SayValidator>(Duplicate = DuplicateStrategy.Append)]
internal sealed class SayValidator(ExcelFunctions excelFunctions) : IQuestValidator
{
    private readonly ExcelFunctions _excelFunctions = excelFunctions;

    public IEnumerable<ValidationIssue> Validate(Quest quest)
    {
        foreach ((QuestSequence Sequence, int StepId, QuestStep Step) in quest.AllSteps().Where(x => x.Step.InteractionType == EInteractionType.Say))
        {
            ChatMessage? chatMessage = Step.ChatMessage;
            if (chatMessage == null)
                continue;

            ReadOnlySeString? excelString = _excelFunctions
                .GetRawDialogueText(quest, chatMessage.ExcelSheet, chatMessage.Key);
            if (excelString == null)
                continue;

            if (excelString.Value.PayloadCount != 1)
            {
                yield return new()
                {
                    ElementId = quest.Id,
                    Sequence = Sequence.Sequence,
                    Step = StepId,
                    Type = EIssueType.InvalidChatMessage,
                    Severity = EIssueSeverity.Error,
                    Description = _LF("Invalid chat message: {0}", excelString.Value)
                };
            }
        }
    }
}
