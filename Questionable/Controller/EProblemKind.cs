namespace Questionable.Controller;

// Sent to the report Worker by name; keep in step with QuestReporter.ApiName.
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
