namespace Questionable.Validation;

public enum EIssueType
{
    None,
    InvalidJsonSchema,
    MissingSequence0,
    MissingSequence,
    DuplicateSequence,
    MissingQuestAccept,
    MissingQuestComplete,
    InstantQuestWithMultipleSteps,
    DuplicateCompletionFlags,
    InvalidNextQuestId,
    QuestDisabled,
    UnexpectedAcceptQuestStep,
    UnexpectedCompleteQuestStep,
    InvalidExcelRef,
    DuplicateSinglePlayerInstance,
    UnusedSinglePlayerInstance,
    InvalidChatMessage
}
