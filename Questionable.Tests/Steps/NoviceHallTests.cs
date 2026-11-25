using ECommons.ExcelServices;
using Questionable.Controller.Steps.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class NoviceHallTests
{
    [Theory]
    [InlineData(Job.PLD, 49, false, true)]
    [InlineData(Job.GLA, 59, false, true)]
    [InlineData(Job.PLD, 48, false, false)]
    [InlineData(Job.PLD, 60, false, false)]
    [InlineData(Job.PLD, 55, true, false)]
    [InlineData(Job.CRP, 55, false, false)]
    [InlineData(Job.MIN, 55, false, false)]
    [InlineData(Job.FSH, 55, false, false)]
    [InlineData(Job.BLU, 55, false, false)]
    [InlineData(Job.ADV, 55, false, false)]
    public void Wanted_LevelJobAndRing(Job job, int level, bool hasRing, bool wanted) =>
        Assert.Equal(wanted, NoviceHall.Wanted(job, level, hasRing));

    [Fact]
    public void RunNoviceHall_OnByDefault() =>
        Assert.True(new Configuration().BaseGeneral.RunNoviceHall);
}
