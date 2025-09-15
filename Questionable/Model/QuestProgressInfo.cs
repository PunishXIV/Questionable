using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using LLib.GameData;
using Questionable.Model.Questing;

namespace Questionable.Model;

internal sealed class QuestProgressInfo
{
    private readonly string _asString;

    public QuestProgressInfo(QuestWork questWork)
    {
        Id = new QuestId(questWork.QuestId);
        Sequence = questWork.Sequence;
        Flags = questWork.Flags;
        Variables = [..questWork.Variables.ToArray()];
        IsHidden = questWork.IsHidden;
        ClassJob = (EClassJob)questWork.AcceptClassJob;
        Tooltip = "";

        Span<byte> qw = questWork.Variables; // 6 bytes
        string repr = "";
        for (int i = 0; i < qw.Length; ++i)
        {
            byte thisbyte = qw[i];
            Tooltip += $"{Convert.ToString(thisbyte, 2).PadLeft(8).Replace(" ", "0")}\n";
            int little = thisbyte & 0xF;
            repr += thisbyte;
            if (little != 0) repr += $"({little})";
            repr += " ";
            if (i % 2 == 1)
                repr += "   ";
        }

        // For combat quests, a sequence to kill 3 enemies works a bit like this:
        // Trigger enemies → 0
        // Kill first enemy → 1
        // Kill second enemy → 2
        // Last enemy → increase sequence, reset variable to 0
        // The order in which enemies are killed doesn't seem to matter.
        // If multiple waves spawn, this continues to count up (e.g. 1 enemy from wave 1, 2 enemies from wave 2, 1 from wave 3) would count to 3 then 0
        _asString = $"QW: {repr.Trim()}";
    }

    public ElementId Id { get; }
    public byte Sequence { get; }
    public ushort Flags { get; init; }
    public List<byte> Variables { get; }
    public bool IsHidden { get; }
    public EClassJob ClassJob { get; }
    public string Tooltip { get; }

    public override string ToString() => _asString;
}
