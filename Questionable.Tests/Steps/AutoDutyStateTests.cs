using Questionable.Controller.Operations;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class AutoDutyStateTests
{
    private const uint Exercise = 1127;
    private static readonly AutoDutyGameState InDuty = new(true, Exercise, false, false, true, false);
    private static readonly AutoDutyGameState Outside = new(true, 129, false, false, false, true);

    private static ExternalOperationStatus RunAndLeave(bool exitIsDone)
    {
        var clock = new TestClock();
        var state = new AutoDutyState(clock);
        state.BeginRequest(Exercise, exitIsDone);
        state.MarkRunRequested();
        state.Observe(false, InDuty);
        state.Observe(true, Outside);
        for (int i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            state.Observe(true, Outside);
        }

        state.ProfileSwitchedBack();
        state.FinishIfSettled();
        return state.Status;
    }

    [Fact]
    public void NoviceExit_CountsAsDone() =>
        Assert.Equal(ExternalOperationStatus.Succeeded, RunAndLeave(exitIsDone: true));

    [Fact]
    public void DutyExit_NeedsCompletion() =>
        Assert.Equal(ExternalOperationStatus.Failed, RunAndLeave(exitIsDone: false));
}
