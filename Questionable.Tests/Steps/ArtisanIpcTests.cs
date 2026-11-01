using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ArtisanIpcTests
{
    private const int ListId = 65537;
    private readonly ICallGateSubscriber<bool> _busy = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<bool> _paused = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<bool> _stop = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<bool> _listRunning = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<bool> _endurance = Substitute.For<ICallGateSubscriber<bool>>();
    private readonly ICallGateSubscriber<Dictionary<int, string>> _lists = Substitute.For<ICallGateSubscriber<Dictionary<int, string>>>();
    private readonly ICallGateSubscriber<int, object> _startList = Substitute.For<ICallGateSubscriber<int, object>>();
    private readonly ICallGateSubscriber<ushort, int, object> _craft = Substitute.For<ICallGateSubscriber<ushort, int, object>>();
    private readonly ArtisanIpc _ipc;
    private readonly CraftingOperationState _operation = new();

    public ArtisanIpcTests()
    {
        var pluginInterface = Substitute.For<IDalamudPluginInterface>();
        pluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy").Returns(_busy);
        pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListPaused").Returns(_paused);
        pluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest").Returns(_stop);
        pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListRunning").Returns(_listRunning);
        pluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus").Returns(_endurance);
        pluginInterface.GetIpcSubscriber<Dictionary<int, string>>("Artisan.GetLists").Returns(_lists);
        pluginInterface.GetIpcSubscriber<int, object>("Artisan.StartListById").Returns(_startList);
        pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem").Returns(_craft);
        _lists.InvokeFunc().Returns(new Dictionary<int, string> { [ListId] = "Quest" });
        _ipc = new ArtisanIpc(pluginInterface, NullLogger<ArtisanIpc>.Instance, _operation);
    }

    [Fact]
    public void BusyProvider_PreservesManualRun()
    {
        _endurance.InvokeFunc().Returns(true);

        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftList(ListId));
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftItem(42, 2));

        _lists.DidNotReceive().InvokeFunc();
        AssertNoStart();
    }

    [Fact]
    public void QueuedWork_BlocksNewRequestsBeforeEnduranceStarts()
    {
        _busy.InvokeFunc().Returns(true);

        Assert.Equal(ArtisanStatus.Queued, _ipc.GetStatus());
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftList(ListId));
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftItem(42, 2));
        AssertNoStart();
    }

    [Fact]
    public void PausedProvider_DoesNotStartOrResumeWork()
    {
        _paused.InvokeFunc().Returns(true);

        Assert.Equal(ArtisanStatus.Paused, _ipc.GetStatus());
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftList(ListId));
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftItem(42, 2));
        AssertNoStart();
    }

    [Fact]
    public void StopRequest_BlocksOtherwiseIdleProvider()
    {
        _stop.InvokeFunc().Returns(true);

        Assert.False(_ipc.IsReady());
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftList(ListId));
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftItem(42, 2));
        AssertNoStart();
    }

    [Fact]
    public void StatusFailure_IsUnavailableNotIdleOrMissing()
    {
        _busy.InvokeFunc().Returns(_ => throw new InvalidOperationException("Provider unloaded"));

        Assert.Equal(ArtisanStatus.Unavailable, _ipc.GetStatus());
        Assert.Equal(ArtisanStartResult.Unavailable, _ipc.CraftList(ListId));
        Assert.Equal(ArtisanStartResult.Unavailable, _ipc.CraftItem(42, 2));
        AssertNoStart();
    }

    [Fact]
    public void MissingList_RequiresSuccessfulCatalogueQuery()
    {
        _lists.InvokeFunc().Returns(new Dictionary<int, string>());

        Assert.Equal(ArtisanStartResult.MissingList, _ipc.CraftList(ListId));
        AssertNoStart();
    }

    [Fact]
    public void CatalogueFailure_DoesNotClaimListIsMissing()
    {
        _lists.InvokeFunc().Returns(_ => throw new InvalidOperationException("Catalogue unavailable"));

        Assert.Equal(ArtisanStartResult.Failed, _ipc.CraftList(ListId));
        AssertNoStart();
    }

    [Fact]
    public void BecomingBusyDuringCatalogueQuery_DoesNotStart()
    {
        _busy.InvokeFunc().Returns(false, true);

        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftList(ListId));
        AssertNoStart();
    }

    [Fact]
    public void ListActionReturning_IsOnlySubmission()
    {
        _startList.When(x => x.InvokeAction(ListId)).Do(_ => Assert.True(_operation.HasOperation));
        Assert.Equal(ArtisanStartResult.Submitted, _ipc.CraftList(ListId));
        Assert.True(_operation.HasOperation);
        Assert.False(_operation.RequiresReconciliation);
        _startList.Received(1).InvokeAction(ListId);
        _craft.DidNotReceiveWithAnyArgs().InvokeAction(default, default);
    }

    [Fact]
    public void ListActionThrowing_DoesNotAllowFallback()
    {
        _startList.When(x => x.InvokeAction(ListId)).Do(_ => throw new InvalidOperationException("List ID does not exist."));

        Assert.Equal(ArtisanStartResult.Failed, _ipc.CraftList(ListId));
        Assert.True(_operation.HasOperation);
        Assert.True(_operation.RequiresReconciliation);
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftItem(42, 2));
        _craft.DidNotReceiveWithAnyArgs().InvokeAction(default, default);
    }

    [Fact]
    public void RecipeActionThrowing_IsReportedAsFailure()
    {
        _craft.When(x => x.InvokeAction(42, 2)).Do(_ => throw new InvalidOperationException("Recipe rejected"));

        Assert.Equal(ArtisanStartResult.Failed, _ipc.CraftItem(42, 2));
        Assert.True(_operation.HasOperation);
        Assert.True(_operation.RequiresReconciliation);
    }

    [Fact]
    public void RecipeSubmission_PassesSynthesisCountUnchanged()
    {
        _craft.When(x => x.InvokeAction(42, 2)).Do(_ => Assert.True(_operation.HasOperation));
        Assert.Equal(ArtisanStartResult.Submitted, _ipc.CraftItem(42, 2));
        _craft.Received(1).InvokeAction(42, 2);
    }

    [Fact]
    public void PendingSubmission_BlocksDuplicateAndFallbackEvenWhileProviderStillIdle()
    {
        Assert.Equal(ArtisanStartResult.Submitted, _ipc.CraftList(ListId));
        Guid? originalId = _operation.OperationId;

        Assert.Equal(ArtisanStatus.Idle, _ipc.GetStatus());
        Assert.False(_ipc.IsReady());
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftList(ListId));
        Assert.Equal(ArtisanStartResult.Busy, _ipc.CraftItem(42, 2));
        Assert.Equal(originalId, _operation.OperationId);
        _startList.Received(1).InvokeAction(ListId);
        _craft.DidNotReceiveWithAnyArgs().InvokeAction(default, default);
    }

    [Fact]
    public void ConfirmedMissingList_CanFallBackWithoutRetainingAnOperation()
    {
        _lists.InvokeFunc().Returns(new Dictionary<int, string>());
        Assert.Equal(ArtisanStartResult.MissingList, _ipc.CraftList(ListId));
        Assert.False(_operation.HasOperation);

        Assert.Equal(ArtisanStartResult.Submitted, _ipc.CraftItem(42, 2));
        Assert.True(_operation.HasOperation);
    }

    private void AssertNoStart()
    {
        Assert.False(_operation.HasOperation);
        _startList.DidNotReceiveWithAnyArgs().InvokeAction(default);
        _craft.DidNotReceiveWithAnyArgs().InvokeAction(default, default);
    }
}
