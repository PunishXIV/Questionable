using Questionable.PathData;
using Xunit;

namespace Questionable.Tests.PathData;

public sealed class PathDataManifestTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void OnlySameFormat_IsCompatible(int bundleFormat, bool expected) =>
        Assert.Equal(expected, new PathDataManifest { MinPluginDataFormat = bundleFormat }.IsCompatibleWith(3));
}
