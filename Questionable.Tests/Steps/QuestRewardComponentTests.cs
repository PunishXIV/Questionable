using Questionable.Model.Common;
using Questionable.Windows.JournalComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestRewardComponentTests
{
    [Fact]
    public void Aetheryte_UsesItsOwnName() =>
        Assert.Equal("Gridania", QuestRewardComponent.AetheryteName(EAetheryteLocation.Gridania));

    [Fact]
    public void Shard_UsesAethernetName() =>
        Assert.Equal("[Gridania] Archers' Guild",
            QuestRewardComponent.AetheryteName(EAetheryteLocation.GridaniaArcher));

    [Fact]
    public void Unnamed_FallsBackToEnumName() =>
        Assert.Equal("None", QuestRewardComponent.AetheryteName(EAetheryteLocation.None));
}
