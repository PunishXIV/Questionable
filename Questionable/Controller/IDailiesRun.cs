// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Quest = Questionable.Domain.Quest;

namespace Questionable.Controller;

internal interface IDailiesRun
{
    string? StopReason { get; }
    Quest? Choose(QuestController.QuestProgress? current);
    void Accepted(ElementId id);
    void Completed(ElementId id);
    EDailiesFailure Fail(ElementId id, string reason);
    void Finish(bool completed);
}

internal enum EDailiesFailure
{
    Retry,
    Skip,
    Stop,
}
