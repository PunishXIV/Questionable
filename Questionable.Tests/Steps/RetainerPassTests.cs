using Questionable.Controller;
using Questionable.External;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class RetainerPassTests
{
    private static readonly AutoRetainerIpc.Character[] Characters =
    [
        new() { ContentId = 1, Enabled = true },
        new() { ContentId = 2, WorkshopEnabled = true },
        new() { ContentId = 3 },
    ];

    private static bool AnyReady(ulong[] retainersBack, ulong[] vesselsBack) =>
        RetainerPass.AnyReady(Characters, retainersBack.Contains, vesselsBack.Contains);

    [Fact]
    public void AnyReady_RetainersBack() => Assert.True(AnyReady([1], []));

    [Fact]
    public void AnyReady_VesselsBack() => Assert.True(AnyReady([], [2]));

    [Fact]
    public void AnyReady_NeedsWorkshopForVessels() => Assert.False(AnyReady([], [1]));

    [Fact]
    public void AnyReady_SkipsNonMultiMode() => Assert.False(AnyReady([3], [3]));
}
