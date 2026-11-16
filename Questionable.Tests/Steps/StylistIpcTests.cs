// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class StylistIpcTests
{
    [Fact]
    public void Updates_RequestConditionalEquip()
    {
        NoPluginsInstalled.Ensure();
        using var ipc = new StylistIpc(NullLogger<StylistIpc>.Instance);
        List<(bool? MoveItems, bool? Equip)> requests = [];
        typeof(StylistIpc).GetField("_updateGearset", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ipc, new Action<bool?, bool?>((move, equip) => requests.Add((move, equip))));

        Assert.True(ipc.UpdateGearset());
        Assert.True(ipc.UpdateGearset());

        // Still check for upgrades on each step, but never force an unchanged set to be equipped.
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.True(request.MoveItems);
            Assert.Null(request.Equip);
        });
    }
}
