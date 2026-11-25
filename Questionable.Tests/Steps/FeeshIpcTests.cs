using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class FeeshIpcTests
{
    [Theory]
    [InlineData(null, EFeeshJobState.Unavailable)]
    [InlineData("", EFeeshJobState.None)]
    [InlineData("Idle", EFeeshJobState.None)]
    [InlineData("fishing Merlthor Goby 0/3", EFeeshJobState.Fishing)]
    [InlineData("finishing Merlthor Goby 3/3", EFeeshJobState.Finishing)]
    [InlineData("done Merlthor Goby 3/3", EFeeshJobState.Done)]
    [InlineData("partial Merlthor Goby 1/3: no bait", EFeeshJobState.Partial)]
    [InlineData("failed Merlthor Goby 0/3: no fishing hole", EFeeshJobState.Failed)]
    [InlineData("stopped Merlthor Goby 2/3", EFeeshJobState.Stopped)]
    [InlineData("something new", EFeeshJobState.Unrecognized)]
    internal void Status_ParsesWahToolsLines(string? text, EFeeshJobState expected) =>
        Assert.Equal(expected, FeeshIpc.ParseState(text));
}
