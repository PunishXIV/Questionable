using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using Questionable.Model.Questing;
namespace Questionable.Domain;

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
        ClassJob = (Job)questWork.AcceptClassJob;
        Tooltip = "";

        Span<byte> qw = questWork.Variables; // 6 bytes
        string repr = "";
        for (int i = 0; i < qw.Length; ++i)
        {
            byte thisbyte = qw[i];
            Tooltip += $"{Convert.ToString(thisbyte, 2).PadLeft(8).Replace(" ", "0", StringComparison.InvariantCulture)}\n";
            int upper = thisbyte >> 4;
            int lower = thisbyte & 0xF;
            if (upper != 0 || lower != 0)
            {
                repr += "(";
                if (upper != 0)
                    repr += $"{upper}H";
                if (lower != 0)
                    repr += $"{lower}L";
                repr += ") ";
            }
            else
                repr += "(-) ";

            if (i % 2 == 1)
                repr += "   ";
        }

        // Combat progress increments per kill and resets when the sequence advances; kills across waves count together.
        _asString = $"QW: {repr.Trim()}";
    }

    public ElementId Id { get; }
    public byte Sequence { get; }
    public ushort Flags { get; init; }
    public List<byte> Variables { get; }
    public bool IsHidden { get; }
    public Job ClassJob { get; }
    public string Tooltip { get; }

    public override string ToString() => _asString;
}
