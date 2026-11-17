using Questionable.Data;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AlliedSocietyDataTests
{
    [Theory]
    [InlineData(1379, EAlliedSociety.Sahagin)]
    [InlineData(1409, EAlliedSociety.Sahagin)]
    [InlineData(1389, EAlliedSociety.None)]
    [InlineData(1257, EAlliedSociety.None)]
    public void CommonTurnIn_MatchesDailies(ushort quest, EAlliedSociety society) =>
        Assert.Equal(society, new AlliedSocietyData().GetCommonTurnIn(new QuestId(quest)));
}
