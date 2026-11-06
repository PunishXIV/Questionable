using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestDeathRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeStopsBeforeOrdinaryOrDeathWork(bool unconscious)
    {
        var host = new RecoveryHost { HasQueuedTasks = true, IsUnconscious = unconscious, EscapePressed = true };

        Assert.True(host.Recovery.Update(host));

        host.AssertStopped();
        Assert.Equal(["stop-all:ESC pressed"], host.Effects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeCancelsRecoveryWithAnEmptyQueue(bool revived)
    {
        var host = new RecoveryHost();
        host.BeginRecovery();
        Assert.False(host.HasQueuedTasks);
        if (revived)
            host.Revive();
        host.EscapePressed = true;
        host.Effects.Clear();

        Assert.True(host.Recovery.Update(host));

        host.AssertStopped();
        Assert.Equal(["stop-all:ESC pressed"], host.Effects);
        host.EscapePressed = false;
        host.IsUnconscious = false;
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(host.Recovery.Update(host));
        Assert.Equal(0, host.Retries);
    }

    [Fact]
    public void DisabledEscapePreservesRecoveryAndItsGracePeriod()
    {
        var host = new RecoveryHost { EscapeEnabled = false };
        host.BeginRecovery();
        host.EscapePressed = true;

        Assert.True(host.Recovery.Update(host));
        Assert.True(host.Recovery.IsRecovering);
        host.Revive();
        Assert.True(host.Recovery.Update(host));
        Assert.True(host.Recovery.IsRecovering);
        Assert.Empty(host.Stops);
        Assert.Equal(0, host.Retries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DutyRun_HandlesDeathUntilStopped(bool escape)
    {
        var host = new RecoveryHost
        {
            HasQueuedTasks = true,
            IsUnconscious = true,
            ProviderHandlesDeath = true,
            HasExternalOperation = true,
            EscapePressed = escape,
        };

        Assert.Equal(escape, host.Recovery.Update(host));

        Assert.False(host.Recovery.IsRecovering);
        if (escape)
        {
            host.AssertStopped();
            Assert.Equal(["stop-all:ESC pressed"], host.Effects);
        }
        else
        {
            Assert.True(host.HasQueuedTasks);
            Assert.Empty(host.Effects);
        }
    }

    [Fact]
    public void ClosedWindowDoesNotSuspendManualStepRecovery()
    {
        var host = new RecoveryHost { IsManual = true, IsQuestWindowOpen = false };
        host.BeginRecovery();
        Assert.False(host.Recovery.IsQuestingStopped(host));
        host.Effects.Clear();
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(["confirm-return"], host.Effects);

        host.Revive();
        Assert.False(host.Recovery.IsQuestingStopped(host));
        host.Clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(host.Recovery.Update(host));
        Assert.False(host.Recovery.IsRecovering);
        Assert.Equal(1, host.Retries);
        Assert.False(host.Recovery.IsQuestingStopped(host));
        host.HasQueuedTasks = false;
        Assert.True(host.Recovery.IsQuestingStopped(host));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetOrLogoutPreventsLateRecovery(bool logout)
    {
        var host = new RecoveryHost { IsManual = true, IsQuestWindowOpen = false };
        host.BeginRecovery();
        if (logout)
        {
            host.IsLoggedIn = false;
            Assert.False(host.Recovery.IsQuestingStopped(host));
            Assert.True(host.Recovery.Update(host));
            host.AssertStopped();
        }
        else
            host.Recovery.Reset();

        host.IsLoggedIn = true;
        host.IsUnconscious = false;
        host.IsQuestWindowOpen = true;
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(host.Recovery.Update(host));
        Assert.Equal(0, host.Retries);
        Assert.False(host.Recovery.IsRecovering);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZoningOrMissingPlayerRestartsTheEntireRespawnGrace(bool missingPlayer)
    {
        var host = new RecoveryHost();
        host.BeginRecovery();
        host.Revive();
        host.Clock.Advance(TimeSpan.FromSeconds(3));
        host.IsZoning = !missingPlayer;
        host.HasPlayer = !missingPlayer;

        Assert.True(host.Recovery.Update(host));
        Assert.True(host.Recovery.IsRecovering);
        Assert.Equal(0, host.Retries);
        host.IsZoning = false;
        host.HasPlayer = true;
        Assert.True(host.Recovery.Update(host));
        host.Clock.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(0, host.Retries);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(1, host.Retries);
    }

    [Fact]
    public void FifthDeathStopsInsteadOfRetrying()
    {
        var host = new RecoveryHost();
        for (int i = 0; i < 5; ++i)
            host.CompleteDeath();

        host.AssertStopped();
        Assert.Equal(4, host.Retries);
        Assert.Equal([5], host.ReportedLimits);
        Assert.Equal(["limit:5", "stop-all:Died too many times"], host.Effects.TakeLast(2));
    }

    [Fact]
    public void FailedCleanupStopsBeforeMovementOrReturnConfirmation()
    {
        var host = new RecoveryHost { HasQueuedTasks = true, IsUnconscious = true, CleanupSucceeds = false };

        Assert.True(host.Recovery.Update(host));

        host.AssertStopped();
        Assert.Equal(["clear-tasks", "stop:Task cleanup failed"], host.Effects);
    }

    [Fact]
    public void OtherOperation_StopsBeforeDeathRecovery()
    {
        var host = new RecoveryHost { HasQueuedTasks = true, IsUnconscious = true, HasExternalOperation = true };

        Assert.True(host.Recovery.Update(host));

        host.AssertStopped();
        Assert.Equal(["stop:Died during external operation"], host.Effects);
    }

    [Fact]
    public void MissingPromptDoesNotConsumeConfirmationThrottle()
    {
        var host = new RecoveryHost();
        host.BeginRecovery();
        Assert.Equal(["clear-tasks", "stop-movement", "confirm-return"], host.Effects);
        host.PromptVisible = true;
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(1, host.ConfirmedReturns);
        int checks = host.Effects.Count;
        host.Clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(checks, host.Effects.Count);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        host.PromptVisible = false;
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(checks + 1, host.Effects.Count);
        host.PromptVisible = true;
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(2, host.ConfirmedReturns);
        Assert.Equal(1, host.Effects.Count(x => x == "clear-tasks"));
    }

    [Fact]
    public void RecoveryRetriesOnceAtTheThreeSecondBoundary()
    {
        var host = new RecoveryHost();
        host.BeginRecovery();
        host.Revive();
        host.Clock.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(0, host.Retries);
        host.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(host.Recovery.Update(host));
        Assert.Equal(1, host.Retries);
        Assert.False(host.Recovery.IsRecovering);
        Assert.False(host.Recovery.Update(host));
        Assert.Equal(1, host.Retries);
    }

    [Theory]
    [InlineData(124, 2, 0)]
    [InlineData(123, 3, 0)]
    [InlineData(123, 2, 1)]
    public void ChangedQuestSequenceOrStepStartsANewDeathStreak(ushort questId, byte sequence, int step)
    {
        var host = new RecoveryHost();
        for (int i = 0; i < 4; ++i)
            host.CompleteDeath();
        host.CurrentStep = (new QuestId(questId), sequence, step);
        host.CompleteDeath();

        Assert.Equal(5, host.Retries);
        Assert.Empty(host.Stops);
        Assert.Empty(host.ReportedLimits);
    }

    [Fact]
    public void ResetStartsANewDeathStreakAndClearsPromptThrottle()
    {
        var host = new RecoveryHost { PromptVisible = true };
        for (int i = 0; i < 4; ++i)
            host.CompleteDeath();
        host.BeginRecovery();
        int confirmations = host.ConfirmedReturns;
        host.Recovery.Reset();
        host.BeginRecovery();
        Assert.Equal(confirmations + 1, host.ConfirmedReturns);
        host.Revive();
        host.Clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(host.Recovery.Update(host));

        Assert.Equal(5, host.Retries);
        Assert.Empty(host.Stops);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void IdlePredicateKeepsExistingModeQueueAndWindowRules(bool manual, bool queued, bool window, bool stopped)
    {
        var host = new RecoveryHost { IsManual = manual, HasQueuedTasks = queued, IsQuestWindowOpen = window };
        Assert.Equal(stopped, host.Recovery.IsQuestingStopped(host));
    }

    [Fact]
    public void LogoutDoesNotReadEscapeOrPlayerConditions()
    {
        var host = Substitute.For<IQuestRecoveryHost>();
        host.IsLoggedIn.Returns(false);
        host.EscapeRequested.Returns(_ => throw new InvalidOperationException("Unexpected input read"));
        host.IsUnconscious.Returns(_ => throw new InvalidOperationException("Unexpected condition read"));
        var recovery = new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance, new TestClock());

        Assert.True(recovery.Update(host));

        host.Received(1).StopAll("Logged out");
    }

    [Fact]
    public void RecoveryKeepsTheIdleGateFromReadingWindowVisibility()
    {
        var host = Substitute.For<IQuestRecoveryHost>();
        host.IsManual.Returns(true);
        host.IsLoggedIn.Returns(true);
        host.IsUnconscious.Returns(true);
        host.HasQueuedTasks.Returns(true);
        host.ClearTasks().Returns(true);
        host.IsQuestWindowOpen.Returns(_ => throw new InvalidOperationException("Unexpected window read"));
        var recovery = new QuestRecoveryController(NullLogger<QuestRecoveryController>.Instance, new TestClock());
        Assert.True(recovery.Update(host));
        host.HasQueuedTasks.Returns(false);

        Assert.False(recovery.IsQuestingStopped(host));
    }

    private sealed class RecoveryHost : IQuestRecoveryHost
    {
        public TestClock Clock { get; } = new();
        public QuestRecoveryController Recovery { get; }
        public bool IsManual { get; set; }
        public bool IsQuestWindowOpen { get; set; } = true;
        public bool HasQueuedTasks { get; set; }
        public bool IsLoggedIn { get; set; } = true;
        public bool EscapeEnabled { get; set; } = true;
        public bool EscapePressed { get; set; }
        public bool EscapeRequested => EscapeEnabled && EscapePressed;
        public bool IsUnconscious { get; set; }
        public bool ProviderHandlesDeath { get; set; }
        public bool HasExternalOperation { get; set; }
        public bool IsZoning { get; set; }
        public bool HasPlayer { get; set; } = true;
        public (ElementId QuestId, byte Sequence, int Step)? CurrentStep { get; set; } = (new QuestId(123), 2, 0);
        public bool CleanupSucceeds { get; set; } = true;
        public bool PromptVisible { get; set; }
        public List<string> Effects { get; } = [];
        public List<string> Stops { get; } = [];
        public List<int> ReportedLimits { get; } = [];
        public int Retries { get; private set; }
        public int ConfirmedReturns { get; private set; }

        public RecoveryHost() => Recovery = new(NullLogger<QuestRecoveryController>.Instance, Clock);

        public bool ClearTasks()
        {
            Effects.Add("clear-tasks");
            HasQueuedTasks = false;
            return CleanupSucceeds;
        }

        public void StopMovement() => Effects.Add("stop-movement");

        public bool TryConfirmReturnPrompt()
        {
            Effects.Add("confirm-return");
            if (PromptVisible)
                ++ConfirmedReturns;
            return PromptVisible;
        }

        public void Stop(string reason) => RecordStop("stop:", reason);
        public void StopAll(string reason) => RecordStop("stop-all:", reason);

        private void RecordStop(string effect, string reason)
        {
            Assert.False(Recovery.IsRecovering);
            Effects.Add(effect + reason);
            Stops.Add(reason);
            HasQueuedTasks = false;
            IsManual = true;
        }

        public void ReportDeathLimit(int deaths)
        {
            Effects.Add($"limit:{deaths}");
            ReportedLimits.Add(deaths);
        }

        public void RetryCurrentStep()
        {
            Assert.False(Recovery.IsRecovering);
            Effects.Add("retry");
            ++Retries;
            HasQueuedTasks = true;
        }

        public void BeginRecovery()
        {
            HasQueuedTasks = true;
            IsUnconscious = true;
            Assert.True(Recovery.Update(this));
            Assert.True(Recovery.IsRecovering);
            Assert.False(HasQueuedTasks);
        }

        public void Revive()
        {
            IsUnconscious = false;
            Assert.True(Recovery.Update(this));
            Assert.True(Recovery.IsRecovering);
        }

        public void CompleteDeath()
        {
            BeginRecovery();
            Revive();
            Clock.Advance(TimeSpan.FromSeconds(3));
            Assert.True(Recovery.Update(this));
        }

        public void AssertStopped()
        {
            Assert.False(Recovery.IsRecovering);
            Assert.False(HasQueuedTasks);
            Assert.True(IsManual);
        }
    }
}
