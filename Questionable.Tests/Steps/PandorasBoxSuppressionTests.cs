using Dalamud.Plugin.Ipc.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class PandorasBoxSuppressionTests
{
    private readonly TestClock _clock = new();
    private readonly Dictionary<string, bool> _enabled = new() { ["A"] = true, ["B"] = false };
    private bool _available = true;

    private PandorasBoxIpc.FeatureSuppression Create() => new(_enabled.Keys.ToList(), NullLogger.Instance, _clock);

    private bool? IsEnabled(string feature) => _available ? _enabled[feature] : throw new IpcNotReadyError("PandorasBox");

    private void SetEnabled(string feature, bool enabled)
    {
        if (!_available)
            throw new IpcNotReadyError("PandorasBox");
        _enabled[feature] = enabled;
    }

    private void Update(PandorasBoxIpc.FeatureSuppression suppression, bool active) =>
        suppression.Update(active, providerInstalled: true, IsEnabled, SetEnabled);

    [Fact]
    public void PausesOnlyEnabledFeaturesAndRestoresThemAfterTheRun()
    {
        var suppression = Create();

        Update(suppression, active: true);
        Assert.False(_enabled["A"]);
        Assert.False(_enabled["B"]);
        Assert.Equal(["A"], suppression.OwnedOff);

        Update(suppression, active: false);
        Assert.True(_enabled["A"]);
        Assert.False(_enabled["B"]);
        Assert.Empty(suppression.OwnedOff);
        Assert.False(suppression.HasWork(false));
    }

    [Fact]
    public void UnavailableAtRunStartIsRetriedInsteadOfGivingUp()
    {
        var suppression = Create();
        _available = false;

        Update(suppression, active: true);
        Assert.True(suppression.HasWork(true));

        _available = true;
        Update(suppression, active: true);
        Assert.True(_enabled["A"]); // still within the retry interval

        _clock.Advance(TimeSpan.FromSeconds(5));
        Update(suppression, active: true);
        Assert.False(_enabled["A"]);
        Assert.Equal(["A"], suppression.OwnedOff);
        Assert.False(suppression.HasWork(true));
    }

    [Fact]
    public void FailedRestoreIsKeptAndRetried()
    {
        var suppression = Create();
        Update(suppression, active: true);

        _available = false;
        Update(suppression, active: false);
        Assert.Equal(["A"], suppression.OwnedOff);
        Assert.True(suppression.HasWork(false));

        _available = true;
        _clock.Advance(TimeSpan.FromSeconds(5));
        Update(suppression, active: false);
        Assert.True(_enabled["A"]);
        Assert.Empty(suppression.OwnedOff);
    }

    [Fact]
    public void NextRun_DoesNotReadPausedFeatureAsUserChoice()
    {
        var suppression = Create();
        Update(suppression, active: true);
        _available = false;
        Update(suppression, active: false);

        _available = true;
        Update(suppression, active: true);
        Assert.Equal(["A"], suppression.OwnedOff);

        Update(suppression, active: false);
        Assert.True(_enabled["A"]);
    }

    [Fact]
    public void ForcedRestoreIgnoresTheRetryInterval()
    {
        var suppression = Create();
        Update(suppression, active: true);
        _available = false;
        Update(suppression, active: false);

        _available = true;
        suppression.Update(false, providerInstalled: true, IsEnabled, SetEnabled, force: true);
        Assert.True(_enabled["A"]);
    }

    [Fact]
    public void MissingProvider_MakesNoCalls()
    {
        var suppression = Create();
        Update(suppression, active: true);

        suppression.Update(false, providerInstalled: false, _ => throw new InvalidOperationException(),
            (_, _) => throw new InvalidOperationException());
        Assert.Equal(["A"], suppression.OwnedOff);
    }
}
