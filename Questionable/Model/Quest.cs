using System.Collections.Generic;
using System.Linq;
using Questionable.Model.Questing;
namespace Questionable.Model;

internal sealed class Quest
{
    public enum ESource
    {
        Assembly,
        ProjectDirectory,
        UserDirectory
    }

    public required ElementId Id { get; init; }
    public required QuestRoot Root { get; init; }
    public required IQuestInfo Info { get; init; }
    public required ESource Source { get; init; }

    public QuestSequence? FindSequence(byte currentSequence)
    {
        return Root.QuestSequence.SingleOrDefault(seq => seq.Sequence == currentSequence);
    }

    public IEnumerable<QuestSequence> AllSequences()
    {
        return Root.QuestSequence;
    }

    public IEnumerable<(QuestSequence Sequence, int StepId, QuestStep Step)> AllSteps()
    {
        foreach (QuestSequence sequence in Root.QuestSequence)
        {
            for (int i = 0; i < sequence.Steps.Count; ++i)
            {
                QuestStep step = sequence.Steps[i];
                yield return (sequence, i, step);
            }
        }
    }
}
