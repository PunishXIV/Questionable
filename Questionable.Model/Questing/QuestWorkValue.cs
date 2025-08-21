using System;
using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(QuestWorkConfigConverter))]
public sealed class QuestWorkValue(byte? high, byte? low, EQuestWorkMode mode)
{
    public QuestWorkValue(byte value)
        : this((byte)(value >> 4), (byte)(value & 0xF), EQuestWorkMode.Bitwise)
    {
    }

    public byte? High { get; set; } = high;
    public byte? Low { get; set; } = low;
    public EQuestWorkMode Mode { get; set; } = mode;

    public override string ToString()
    {
        if (High != null && Low != null)
            return ((byte)(High << 4) + Low).ToString();
        else if (High != null)
            return High + "H";
        else if (Low != null)
            return Low + "L";
        else
            return "-";
    }
}
