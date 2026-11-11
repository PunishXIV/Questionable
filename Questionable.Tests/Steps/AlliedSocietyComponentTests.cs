using Questionable.Windows.JournalComponents;
using Xunit;
using EType = Questionable.Windows.JournalComponents.AlliedSocietyComponent.EType;

namespace Questionable.Tests.Steps;

public sealed class AlliedSocietyComponentTests
{
    private static readonly (bool, bool, bool) Combat = (true, false, false);
    private static readonly (bool, bool, bool) Crafters = (false, true, false);
    private static readonly (bool, bool, bool) Gatherers = (false, false, true);
    private static readonly (bool, bool, bool) CraftOrGather = (false, true, true);
    private static readonly (bool, bool, bool) Everyone = (true, true, true);

    private static EType? TypeOf(params (bool, bool, bool)[] quests) =>
        AlliedSocietyComponent.GetSocietyType(quests);

    [Fact]
    public void CombatQuests_AreCombat() => Assert.Equal(EType.Combat, TypeOf(Combat, Combat));

    [Fact]
    public void CrafterQuests_AreCrafting() => Assert.Equal(EType.Crafting, TypeOf(Crafters, Crafters));

    [Fact]
    public void GathererQuests_AreGathering() => Assert.Equal(EType.Gathering, TypeOf(Gatherers));

    [Fact]
    public void CrafterAndGatherer_AreBoth()
    {
        Assert.Equal(EType.CraftingAndGathering, TypeOf(Crafters, Gatherers));
        Assert.Equal(EType.CraftingAndGathering, TypeOf(CraftOrGather));
    }

    [Fact]
    public void OneOddQuest_KeepsTheType() =>
        Assert.Equal(EType.Crafting, TypeOf(Crafters, Crafters, Everyone));

    [Fact]
    public void NoQuests_HasNoType() => Assert.Null(TypeOf());
}
