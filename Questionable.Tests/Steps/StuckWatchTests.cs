using System.Numerics;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Controller.Steps.Movement;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class StuckWatchTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);
    private static readonly (ElementId, byte, int) StepA = (new QuestId(1), 1, 0);
    private static readonly (ElementId, byte, int) StepB = (new QuestId(1), 1, 1);

    private readonly TestClock _clock = new();
    private readonly ITask _task = new WaitAtEnd.NextStepOrSequence();

    private StuckWatch.EResult Tick(StuckWatch watch, (ElementId, byte, int) step, Vector3 position = default,
        bool paused = false, ITask? task = null, int maxTries = 3)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return watch.Update(step, task ?? _task, position, paused, Delay, maxTries);
    }

    private StuckWatch.EResult Wait(StuckWatch watch, (ElementId, byte, int) step, int seconds)
    {
        StuckWatch.EResult result = StuckWatch.EResult.None;
        for (int i = 0; i < seconds && result == StuckWatch.EResult.None; i++)
            result = Tick(watch, step);
        return result;
    }

    [Fact]
    public void NoMove_RetriesAfterDelay()
    {
        var watch = new StuckWatch(_clock);
        Tick(watch, StepA);

        Assert.Equal(StuckWatch.EResult.None, Wait(watch, StepA, 29));
        Assert.Equal(StuckWatch.EResult.Retry, Tick(watch, StepA));
        Assert.Equal(1, watch.Tries);
    }

    [Fact]
    public void Moving_NeverRetries()
    {
        var watch = new StuckWatch(_clock);
        for (int i = 0; i < 120; i++)
            Assert.Equal(StuckWatch.EResult.None, Tick(watch, StepA, new Vector3(i, 0, 0)));
    }

    [Fact]
    public void SmallDrift_StillCountsAsStuck()
    {
        var watch = new StuckWatch(_clock);
        StuckWatch.EResult result = StuckWatch.EResult.None;
        for (int i = 0; i <= 31 && result == StuckWatch.EResult.None; i++)
            result = Tick(watch, StepA, new Vector3(i % 2 * 0.3f, 0, 0));

        Assert.Equal(StuckWatch.EResult.Retry, result);
    }

    [Fact]
    public void Pause_RestartsTimer()
    {
        var watch = new StuckWatch(_clock);
        Tick(watch, StepA);
        Wait(watch, StepA, 20);
        Tick(watch, StepA, paused: true);

        Assert.Equal(StuckWatch.EResult.None, Wait(watch, StepA, 29));
        Assert.Equal(StuckWatch.EResult.Retry, Tick(watch, StepA));
    }

    [Fact]
    public void NewTask_RestartsTimerKeepsTries()
    {
        var watch = new StuckWatch(_clock);
        Tick(watch, StepA);
        Wait(watch, StepA, 30);
        Tick(watch, StepA, task: new WaitAtEnd.NextStepOrSequence());

        Assert.Equal(1, watch.Tries);
        Assert.Equal(StuckWatch.EResult.None, Tick(watch, StepA, task: new WaitAtEnd.NextStepOrSequence()));
    }

    [Fact]
    public void PastLimit_Stops()
    {
        var watch = new StuckWatch(_clock);
        Tick(watch, StepA);

        for (int i = 1; i <= 3; i++)
        {
            Assert.Equal(StuckWatch.EResult.Retry, Wait(watch, StepA, 30));
            Assert.Equal(i, watch.Tries);
        }

        Assert.Equal(StuckWatch.EResult.Stop, Wait(watch, StepA, 30));
    }

    [Fact]
    public void NewStep_ResetsTries()
    {
        var watch = new StuckWatch(_clock);
        Tick(watch, StepA);
        Wait(watch, StepA, 30);
        Tick(watch, StepB);

        Assert.Equal(0, watch.Tries);
        Assert.Equal(StuckWatch.EResult.Retry, Wait(watch, StepB, 30));
        Assert.Equal(1, watch.Tries);
    }

    [Theory]
    [InlineData(EInteractionType.UseItem)]
    [InlineData(EInteractionType.Combat)]
    [InlineData(null)]
    public void Movement_RetriesOnAnyStep(EInteractionType? stepType) =>
        Assert.True(StuckWatch.CanRetry(stepType, new LandTask()));

    [Theory]
    [InlineData(EInteractionType.Interact, true)]
    [InlineData(EInteractionType.AcceptQuest, true)]
    [InlineData(EInteractionType.UseItem, false)]
    [InlineData(EInteractionType.PurchaseItem, false)]
    [InlineData(EInteractionType.CleanUpOtherQuests, false)]
    [InlineData(EInteractionType.Combat, false)]
    public void AfterWait_OnlySafeStepsRetry(EInteractionType stepType, bool expected) =>
        Assert.Equal(expected, StuckWatch.CanRetry(stepType, new WaitAtEnd.QuestCompleted(new QuestId(1))));

    [Fact]
    public void Say_RetriesOnlyOnSayStep()
    {
        Assert.True(StuckWatch.CanRetry(EInteractionType.Say, new Say.Task("hi")));
        Assert.False(StuckWatch.CanRetry(EInteractionType.Duty, new Say.Task("hi")));
    }

    [Fact]
    public void OtherTasks_NeverRetry()
    {
        Assert.False(StuckWatch.CanRetry(EInteractionType.Interact, new WaitAtEnd.NextStepOrSequence()));
        Assert.False(StuckWatch.CanRetry(EInteractionType.Interact, new WaitAtStart.WaitDelay(TimeSpan.FromSeconds(5))));
        Assert.False(StuckWatch.CanRetry(EInteractionType.Interact, null));
    }
}
