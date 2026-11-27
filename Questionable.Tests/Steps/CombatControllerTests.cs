using Questionable.Controller;
using Questionable.Model.Common;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CombatControllerTests
{
    [Theory]
    [InlineData(ECombatModule.BossMod, "Boss Mod is the combat plugin")]
    [InlineData(ECombatModule.WrathCombo, "Wrath Combo is the combat plugin")]
    [InlineData(ECombatModule.RotationSolverReborn, "Rotation Solver Reborn is the combat plugin")]
    [InlineData(ECombatModule.None, "No combat plugin is set up")]
    public void NoModuleMessage_NamesPickedPlugin(ECombatModule picked, string start) =>
        Assert.StartsWith(start, CombatController.NoModuleMessage(picked), StringComparison.Ordinal);
}
