using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.CombatModules;
using Questionable.Controller.Steps;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ItemUseModuleTests
{
    private readonly ICombatModule _rotation = Substitute.For<ICombatModule>();
    private readonly ItemUseModule _module;
    private readonly CombatController.CombatData _data = new()
    {
        ElementId = null,
        Sequence = 0,
        CompletionQuestVariablesFlags = [],
        SpawnType = EEnemySpawnType.AfterInteraction,
        KillEnemyDataIds = [],
        ComplexCombatDatas = [],
        CombatItemUse = new CombatItemUse { ItemId = 1 },
    };

    public ItemUseModuleTests()
    {
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IEnumerable<ICombatModule>)).Returns(new ICombatModule[] { _rotation });
        _rotation.CanHandleFight(_data).Returns(true);
        _rotation.Start(_data).Returns(true);
        _rotation.Stop().Returns(true);
        _module = new ItemUseModule(services, Substitute.For<ICondition>(), null!, NullLogger<ItemUseModule>.Instance);
        Assert.True(_module.CanHandleFight(_data));
    }

    [Fact]
    public void RejectedInitialStart_RetainsDelegateForRollback()
    {
        _rotation.Start(_data).Returns(false);
        Assert.Throws<TaskException>(() => _module.Start(_data));
        Assert.True(_module.Stop());
        _rotation.Received(1).Stop();
    }

    [Fact]
    public void RejectedRestart_AfterItemPauseDoesNotClaimRotationIsRunning()
    {
        Assert.True(_module.Start(_data));
        _module.StopRotation();
        _rotation.Start(_data).Returns(false);
        Assert.Throws<TaskException>(() => _module.StartRotation());
        _rotation.Stop().Returns(false);
        Assert.False(_module.Stop());
        _rotation.Stop().Returns(true);
        Assert.True(_module.Stop());
    }

    [Fact]
    public void FailedPause_PreventsItemUseAndRetainsDelegateForRetry()
    {
        Assert.True(_module.Start(_data));
        _rotation.Stop().Returns(false);
        Assert.Throws<TaskException>(() => _module.StopRotation());
        Assert.False(_module.Stop());
        _rotation.Stop().Returns(true);
        Assert.True(_module.Stop());
    }

    [Fact]
    public void OwnershipCheck_DelegatesOnlyWhileRotationIsActive()
    {
        Assert.True(_module.Start(_data));
        _rotation.When(x => x.VerifyActive()).Do(_ => throw new TaskException("lease cancelled"));
        Assert.Throws<TaskException>(() => _module.VerifyActive());
        _module.StopRotation();
        _module.VerifyActive();
        _rotation.Received(1).VerifyActive();
    }
}
