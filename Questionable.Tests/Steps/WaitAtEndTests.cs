// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @eternalwaitt

using Questionable.Controller.Steps.Shared;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class WaitAtEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaitDelay_UsesConfiguredDamageInterruption(bool interruptOnDamage)
    {
        WaitAtEnd.WaitDelay task = new() { InterruptOnDamage = interruptOnDamage };
        WaitAtEnd.WaitDelayExecutor executor = new();

        Assert.True(executor.Start(task));
        Assert.Equal(interruptOnDamage, executor.ShouldInterruptOnDamage());
    }
}
