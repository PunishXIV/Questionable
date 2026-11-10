using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
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

        Span<byte> bytes = questWork.Variables; // 6 bytes
        string text = "";
        for (int i = 0; i < bytes.Length; ++i)
        {
            byte value = bytes[i];
            Tooltip += $"{Convert.ToString(value, 2).PadLeft(8).Replace(" ", "0", StringComparison.InvariantCulture)}\n";
            int upper = value >> 4;
            int lower = value & 0xF;
            if (upper != 0 || lower != 0)
            {
                text += "(";
                if (upper != 0)
                    text += $"{upper}H";
                if (lower != 0)
                    text += $"{lower}L";
                text += ") ";
            }
            else
                text += "(-) ";

            if (i % 2 == 1)
                text += "   ";
        }

        // Combat progress increments per kill and resets when the sequence advances; kills across waves count together.
        _asString = $"QW: {text.Trim()}";
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
