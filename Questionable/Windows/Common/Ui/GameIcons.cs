using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Data.Files;

namespace Questionable.Windows.Common.Ui;

[RegisterSingleton]
internal sealed class GameIcons(
    IDataManager dataManager,
    ITextureProvider textureProvider,
    ILogger<GameIcons> logger) : IDisposable
{
    private readonly Dictionary<(uint IconId, int Size), IDalamudTextureWrap?> _cache = [];

    public bool DrawInline(uint iconId)
    {
        int size = (int)MathF.Round(ImGui.GetTextLineHeightWithSpacing());
        if (size <= 0 || Get(iconId, size) is not { } texture)
            return false;

        Vector2 position = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(position.X), MathF.Round(position.Y)));
        ImGui.Image(texture.Handle, new Vector2(size));
        ImGui.SameLine();
        return true;
    }

    private IDalamudTextureWrap? Get(uint iconId, int size)
    {
        if (_cache.TryGetValue((iconId, size), out IDalamudTextureWrap? cached))
            return cached;

        IDalamudTextureWrap? texture = null;
        try
        {
            if (textureProvider.TryGetIconPath(new GameIconLookup(iconId), out string? path) &&
                dataManager.GetFile<TexFile>(path) is { } file)
                texture = Resample(file, iconId, size);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not resample icon {IconId} to {Size}px", iconId, size);
        }

        _cache[(iconId, size)] = texture;
        return texture;
    }

    private IDalamudTextureWrap Resample(TexFile file, uint iconId, int size)
    {
        // these are bgra, not rgba
        byte[] bitmap = Downscale(file.ImageData, file.Header.Width, file.Header.Height, size);
        return textureProvider.CreateFromRaw(RawImageSpecification.Bgra32(size, size), bitmap,
            $"Questionable icon {iconId}@{size}");
    }

    // GPU scaling 80px icons to text height looks grainy, so each pixel averages its source area.
    internal static byte[] Downscale(byte[] bgra, int width, int height, int size)
    {
        byte[] result = new byte[size * size * 4];
        double scaleX = (double)width / size;
        double scaleY = (double)height / size;
        for (int ty = 0; ty < size; ty++)
        {
            double y0 = ty * scaleY;
            double y1 = y0 + scaleY;
            for (int tx = 0; tx < size; tx++)
            {
                double x0 = tx * scaleX;
                double x1 = x0 + scaleX;
                double blue = 0, green = 0, red = 0, alpha = 0, area = 0;
                for (int sy = (int)y0; sy < Math.Min(height, (int)Math.Ceiling(y1)); sy++)
                {
                    double weightY = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    for (int sx = (int)x0; sx < Math.Min(width, (int)Math.Ceiling(x1)); sx++)
                    {
                        double weight = weightY * (Math.Min(x1, sx + 1) - Math.Max(x0, sx));
                        int i = (sy * width + sx) * 4;
                        double covered = bgra[i + 3] * weight;
                        blue += bgra[i] * covered;
                        green += bgra[i + 1] * covered;
                        red += bgra[i + 2] * covered;
                        alpha += covered;
                        area += weight;
                    }
                }

                int o = (ty * size + tx) * 4;
                if (alpha > 0)
                {
                    result[o] = (byte)Math.Round(blue / alpha);
                    result[o + 1] = (byte)Math.Round(green / alpha);
                    result[o + 2] = (byte)Math.Round(red / alpha);
                }

                result[o + 3] = area > 0 ? (byte)Math.Round(alpha / area) : (byte)0;
            }
        }

        return result;
    }

    public void Dispose()
    {
        foreach (IDalamudTextureWrap? texture in _cache.Values)
            texture?.Dispose();
        _cache.Clear();
    }
}
