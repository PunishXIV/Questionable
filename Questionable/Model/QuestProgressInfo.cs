using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using LLib.GameData;
using Questionable.Model.Questing;
using System.Collections.Generic;

namespace Questionable.Model;

internal sealed class QuestProgressInfo
{
    private readonly string _asString;

    public QuestProgressInfo(QuestWork questWork)
    {
        Id = new QuestId(questWork.QuestId);
        Sequence = questWork.Sequence;
        Flags = questWork.Flags;
        Variables = [.. questWork.Variables.ToArray()];
        IsHidden = questWork.IsHidden;
        ClassJob = (EClassJob)questWork.AcceptClassJob;

        var qw = questWork.Variables;
        string vars = "";
        for (int i = 0; i < qw.Length; ++i)
        {
            vars += qw[i] + " ";
            if (i % 2 == 1)
                vars += "   ";
        }

        // For combat quests, a sequence to kill 3 enemies works a bit like this:
        // Trigger enemies → 0
        // Kill first enemy → 1
        // Kill second enemy → 2
        // Last enemy → increase sequence, reset variable to 0
        // The order in which enemies are killed doesn't seem to matter.
        // If multiple waves spawn, this continues to count up (e.g. 1 enemy from wave 1, 2 enemies from wave 2, 1 from wave 3) would count to 3 then 0
        _asString = $"QW: {vars.Trim()}";
    }

    public ElementId Id { get; }
    public byte Sequence { get; }
    public ushort Flags { get; init; }
    public List<byte> Variables { get; }
    public bool IsHidden { get; }
    public EClassJob ClassJob { get; }

    public override string ToString() => _asString;
}
