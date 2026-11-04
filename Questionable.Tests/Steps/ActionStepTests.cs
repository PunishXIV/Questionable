using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.Steps;
using Questionable.Controller.Steps.Interactions;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ActionStepTests
{
    private const byte SpellActionType = 1;
    private const byte ItemActionType = 2;
    private readonly InterruptHandler.ActionConfirmations _confirmations = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedConfirmationDoesNotCompleteAcceptedOrRejectedAction(bool accepted)
    {
        var executor = CreateExecutor();
        executor.RecordAttempt(accepted, accepted ? Request(41) : null, _confirmations.Version);
        _confirmations.Observe(42);

        Assert.Equal(ETaskResult.StillRunning, executor.Update());
    }

    [Fact]
    public void RejectedActionCannotBorrowAMatchingSequence()
    {
        var executor = CreateExecutor();
        executor.RecordAttempt(false, Request(41), _confirmations.Version);
        _confirmations.Observe(41);

        Assert.Equal(ETaskResult.StillRunning, executor.Update());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedConfirmationDoesNotHideExhaustedRejectedAttempts(bool withFlags)
    {
        var executor = CreateExecutor(withFlags);
        for (int i = 0; i < 10; ++i)
            executor.RecordAttempt(false, null, _confirmations.Version);
        _confirmations.Observe(42);

        Assert.Throws<TaskException>(() => executor.Update(() => ETaskResult.StillRunning));
    }

    [Fact]
    public void MatchingConfirmationSurvivesALaterUnrelatedEffect()
    {
        var executor = CreateExecutor();
        executor.RecordAttempt(true, Request(41), _confirmations.Version);
        _confirmations.Observe(41);
        _confirmations.Observe(42);

        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
    }

    [Fact]
    public void StaleConfirmationCannotSatisfyAReusedSequence()
    {
        _confirmations.Observe(41);
        var executor = CreateExecutor();
        executor.RecordAttempt(true, Request(41), _confirmations.Version);

        Assert.Equal(ETaskResult.StillRunning, executor.Update());
        _confirmations.Observe(41);
        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
    }

    [Fact]
    public void LateConfirmationOfAcceptedAttemptSurvivesRejectedRetry()
    {
        var executor = CreateExecutor();
        executor.RecordAttempt(true, Request(41), _confirmations.Version);
        executor.RecordAttempt(false, null, _confirmations.Version);
        _confirmations.Observe(41);

        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
    }

    [Fact]
    public void AcceptedWithoutDispatchedSequenceIsNotConfirmedByOtherActions()
    {
        var executor = CreateExecutor();
        executor.RecordAttempt(true, Request(), _confirmations.Version);
        _confirmations.Observe(42);

        Assert.Equal(ETaskResult.StillRunning, executor.Update());
    }

    [Fact]
    public void MatchingActionStillWaitsForQuestFlagsAndRetainsConfirmation()
    {
        var executor = CreateExecutor(withFlags: true);
        executor.RecordAttempt(true, Request(41), _confirmations.Version);
        _confirmations.Observe(41);

        Assert.Equal(ETaskResult.StillRunning, executor.Update(() => ETaskResult.StillRunning));
        for (ushort i = 100; i < 140; ++i)
            _confirmations.Observe(i);
        Assert.Equal(ETaskResult.StillRunning, executor.Update(() => ETaskResult.StillRunning));
        Assert.Equal(ETaskResult.TaskComplete, executor.Update(() => ETaskResult.TaskComplete));
    }

    [Fact]
    public void ZeroSequenceDoesNotCountAsPlayerActionConfirmation()
    {
        _confirmations.Observe(0);
        Assert.Equal(0, _confirmations.Version);
        Assert.False(_confirmations.Contains(Request(0), -1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedRequestCompletesOnMatchingSpellAndTarget(bool accepted)
    {
        var executor = CreateExecutor();
        executor.RecordAttempt(accepted, Request(), _confirmations.Version);
        _confirmations.Observe(41, SpellActionType, 120, 999); // Same spell on another target.
        _confirmations.Observe(42, SpellActionType, 135, 123); // Different spell on the requested target.
        _confirmations.Observe(43, ItemActionType, 120, 123); // Different action type.
        Assert.Equal(ETaskResult.StillRunning, executor.Update());

        _confirmations.Observe(44, SpellActionType, 120, 123);
        Assert.Equal(ETaskResult.TaskComplete, executor.Update());
    }

    [Fact]
    public void QueuedRequestCannotUseMatchingEffectFromBeforeAttempt()
    {
        _confirmations.Observe(41, SpellActionType, 120, 123);
        var executor = CreateExecutor();
        executor.RecordAttempt(true, Request(), _confirmations.Version);

        Assert.Equal(ETaskResult.StillRunning, executor.Update());
    }

    [Fact]
    public void DispatchedRequestDoesNotBorrowSameSpellFromAnotherRequest()
    {
        var executor = CreateExecutor();
        executor.RecordAttempt(true, Request(41), _confirmations.Version);
        _confirmations.Observe(42, SpellActionType, 120, 123);

        Assert.Equal(ETaskResult.StillRunning, executor.Update());
    }

    private static InterruptHandler.ActionRequest Request(ushort? sequence = null) =>
        new(SpellActionType, 120, 123, sequence);

    private ActionStep.UseOnObjectExecutor CreateExecutor(bool withFlags = false)
    {
        // Avoid installing a native hook; use its real managed confirmation store and executor update path.
        var interrupt = (InterruptHandler)RuntimeHelpers.GetUninitializedObject(typeof(InterruptHandler));
        typeof(InterruptHandler).GetField("<Confirmations>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(interrupt, _confirmations);
        var condition = Substitute.For<ICondition>();
        condition[ConditionFlag.Casting].Returns(true); // Keep native retry submission outside this test boundary.
        var executor = new ActionStep.UseOnObjectExecutor(null!, interrupt, condition,
            NullLogger<ActionStep.UseOnObject>.Instance);
        var task = new ActionStep.UseOnObject(123, QuestTestData.CreateQuest(new QuestId(92)), EAction.Cure,
            withFlags ? [new QuestWorkValue(16), null, null, null, null, null] : []);
        typeof(TaskExecutor<ActionStep.UseOnObject>).GetProperty("Task", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(executor, task);
        return executor;
    }
}
