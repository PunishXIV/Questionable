using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
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
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PlayerMove_StopsOnlyWhileRunning(bool running, bool stops)
    {
        var host = new RecoveryHost { HasQueuedTasks = running, PlayerMoved = true };

        Assert.Equal(stops, host.Recovery.Update(host));

        Assert.Equal(stops ? ["stop-all:Player moved"] : [], host.Effects);
    }

    [Fact]
    public void DisabledEscape_KeepsRecoveryAndGrace()
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
    public void ClosedWindow_KeepsManualRecovery()
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
    public void OtherOperation_StopsBeforeDeathRecovery()
    {
        var host = new RecoveryHost { HasQueuedTasks = true, IsUnconscious = true, HasExternalOperation = true };

        Assert.True(host.Recovery.Update(host));

        host.AssertStopped();
        Assert.Equal(["stop:Died during external operation"], host.Effects);
    }

    [Fact]
    public void Recovery_RetriesOnceAtThreeSeconds()
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
    public void ChangedProgress_StartsNewStreak(ushort questId, byte sequence, int step)
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
        public bool PlayerMoved { get; set; }
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
            Effects.Add(string.Create(CultureInfo.InvariantCulture, $"limit:{deaths}"));
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
