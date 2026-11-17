using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Common;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AlliedSocietyDataTests
{
    [Theory]
    [InlineData(1379, EAlliedSociety.Sahagin, true, EAlliedSociety.Sahagin)]
    [InlineData(1389, EAlliedSociety.Sahagin, false, EAlliedSociety.None)]
    [InlineData(1257, EAlliedSociety.Sylphs, true, EAlliedSociety.None)]
    [InlineData(1569, EAlliedSociety.Ixal, true, EAlliedSociety.None)]
    [InlineData(100, EAlliedSociety.None, true, EAlliedSociety.None)]
    public void CommonTurnIn_RepeatableSocietyOnly(ushort quest, EAlliedSociety society, bool repeatable,
        EAlliedSociety expected) =>
        Assert.Equal(expected, new AlliedSocietyData().GetCommonTurnIn(TestQuestInfo.Create(quest, "Quest")
            .With(nameof(QuestInfo.AlliedSociety), society)
            .With(nameof(QuestInfo.IsRepeatable), repeatable)));
}
