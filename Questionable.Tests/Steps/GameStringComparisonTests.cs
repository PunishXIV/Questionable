using Questionable.Utils;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class GameStringComparisonTests
{
    [Theory]
    [InlineData("Are you <string(gstr1)>?", "Are you Lucan Beltz?", true)]
    [InlineData("Are you Lucan Beltz?", "Are you <string(gstr1)>?", true)]
    [InlineData("Are you <string(gstr1)>?", "Who are you?", false)]
    [InlineData("<italic(1)>Proceed?<italic(0)>", "Proceed?", true)]
    [InlineData("Proceed?", "Proceed? (Yes)", false)]
    [InlineData("Pay 1,000 gil.", "Pay 1,000 gil.", true)]
    public void Matches_TreatsMacrosAsFilledText(string excel, string shown, bool expected) =>
        Assert.Equal(expected, GameStringComparison.Matches(excel, shown));
}
