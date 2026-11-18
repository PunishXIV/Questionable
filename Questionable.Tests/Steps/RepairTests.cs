using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class RepairTests
{
    [Theory]
    [InlineData(2, 0, 1000394u)]
    [InlineData(0, 3, 1004416u)]
    [InlineData(0, 0, 1003251u)]
    public void Mender_GrandCompanyThenCity(byte grandCompany, byte startTown, uint npcId) =>
        Assert.Equal(npcId, Repair.ChooseMender(grandCompany, startTown).NpcId);

    [Theory]
    [InlineData(8700, 30, true)]
    [InlineData(9000, 30, false)]
    [InlineData(30000, 30, false)]
    public void Condition_WornBelowPercent(ushort condition, int belowPercent, bool worn) =>
        Assert.Equal(worn, Repair.IsWorn(condition, belowPercent));
}
