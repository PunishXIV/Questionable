using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Questionable.Model.Common;
namespace Questionable.Model.Questing;

public sealed class QuestSequence : IJsonOnDeserialized
{
    [AlwaysSerialize]
    public byte Sequence { get; set; }

    // File-only default for steps that leave out their own TerritoryId; cleared once copied into them.
    public uint? TerritoryId { get; set; }

    public string? Comment { get; set; }
    public List<QuestStep> Steps { get; set; } = new();

    public void OnDeserialized()
    {
        if (TerritoryId is not { } territoryId)
            return;

        foreach (QuestStep step in Steps)
        {
            if (step.TerritoryId == 0)
                step.TerritoryId = territoryId;
        }

        TerritoryId = null;
    }

    public QuestStep? FindStep(int step)
    {
        if (step < 0 || step >= Steps.Count)
            return null;

        return Steps[step];
    }

    public QuestStep? LastStep() => Steps.LastOrDefault();
}
