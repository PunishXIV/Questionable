using System.Collections.Generic;
using System.Linq;

namespace Questionable.Model.Questing;

public sealed class QuestSequence
{
    public byte Sequence { get; set; }
    public string? Comment { get; set; }
    public List<QuestStep> Steps { get; set; } = new();

    public QuestStep? FindStep(int step)
    {
        if (step < 0 || step >= Steps.Count)
            return null;

        return Steps[step];
    }

    public QuestStep? LastStep() => Steps.LastOrDefault();
}
