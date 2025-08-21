using System.Runtime.InteropServices;

namespace Questionable.GameStructs;

[StructLayout(LayoutKind.Explicit, Size = 0x500)]
internal struct AgentSatisfactionSupply2
{
    /// <summary>
    /// at 4/5 hearts
    ///   3 deliveries: 540 / 1080
    ///   4 deliveries: 720 / 1080
    ///   5 deliveries: 900 / 1080
    /// 5 hearts: 0 / 0
    /// </summary>
    [FieldOffset(0x70)] public ushort CurrentSatisfaction;
    [FieldOffset(0x72)] public ushort MaxSatisfaction;

    public int CalculateTurnInsToNextRank(int maxTurnIns)
    {
        if (MaxSatisfaction == 0)
            return maxTurnIns;

        return maxTurnIns * (MaxSatisfaction - CurrentSatisfaction) / MaxSatisfaction;
    }
}
