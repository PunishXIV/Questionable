using Questionable.Windows.Common.Ui;
using Xunit;

namespace Questionable.Tests.Windows;

public sealed class GameIconsTests
{
    private static byte[] Pixel(byte b, byte g, byte r, byte a) => [b, g, r, a];

    [Fact]
    public void Downscale_AveragesBlock()
    {
        byte[] source = [.. Pixel(0, 0, 0, 255), .. Pixel(200, 100, 40, 255), .. Pixel(0, 0, 0, 255), .. Pixel(200, 100, 40, 255)];

        Assert.Equal(Pixel(100, 50, 20, 255), GameIcons.Downscale(source, 2, 2, 1));
    }

    [Fact]
    public void Downscale_ClearPixelsKeepColour()
    {
        byte[] source = [.. Pixel(0, 0, 0, 0), .. Pixel(200, 100, 40, 255), .. Pixel(0, 0, 0, 0), .. Pixel(200, 100, 40, 255)];

        Assert.Equal(Pixel(200, 100, 40, 128), GameIcons.Downscale(source, 2, 2, 1));
    }

    [Fact]
    public void Downscale_UnevenScaleSplitsPixels()
    {
        byte[] source = [.. Pixel(0, 0, 0, 255), .. Pixel(90, 90, 90, 255), .. Pixel(180, 180, 180, 255)];

        byte[] result = GameIcons.Downscale(source, 3, 1, 2);

        // The target is square, so this is its first row.
        Assert.Equal([.. Pixel(30, 30, 30, 255), .. Pixel(150, 150, 150, 255)], result[..8]);
    }
}
