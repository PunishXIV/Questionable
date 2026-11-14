namespace Questionable.Controller;

// Sent to the report Worker by name (QuestReporter.ApiName); keep the two in step.
internal enum EProblemKind
{
    Stuck,
    Deaths,
    QuestBattle,
    MissingPath,
    PlayerStopped,
    WrongTarget,
    Other,
}
