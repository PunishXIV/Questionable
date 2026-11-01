using Questionable.Data;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class SoloDutyMetadataTests
{
    [Theory]
    [InlineData("QUESTBATTLE0", null, true, 0)]
    [InlineData("QUESTBATTLE1", null, true, 1)]
    [InlineData("QUESTBATTLE2", null, true, 2)]
    [InlineData("INSTANCEDUNGEON0", 11u, true, 0)]
    [InlineData("INSTANCEDUNGEON1", 11u, true, 1)]
    [InlineData("INSTANCEDUNGEON0", 2u, false, 0)]
    [InlineData("INSTANCEDUNGEON0", null, false, 0)]
    [InlineData("QUESTBATTLE", null, false, 0)]
    [InlineData("QUESTBATTLE256", null, false, 0)]
    [InlineData("QUESTBATTLE+1", null, false, 0)]
    [InlineData("INSTANCEDUNGEON_NAME0", 11u, false, 0)]
    public void Discovery_UsesQuestInstructionsAndSeasonalMetadata(string instruction, uint? instanceType, bool expected, byte expectedIndex)
    {
        Assert.Equal(expected, TerritoryData.TryGetSoloDutyIndex(instruction, instanceType, out byte index));
        Assert.Equal(expectedIndex, index);
    }
}
