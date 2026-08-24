using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Data.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Questionable.Windows.Common.Ui;

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
        using Image<Bgra32> image =
            Image.LoadPixelData<Bgra32>(file.ImageData, file.Header.Width, file.Header.Height);
        image.Mutate(x => x.Resize(size, size, KnownResamplers.Lanczos3));

        byte[] bitmap = new byte[size * size * 4];
        image.CopyPixelDataTo(bitmap);

        return textureProvider.CreateFromRaw(RawImageSpecification.Bgra32(size, size), bitmap,
            $"Questionable icon {iconId}@{size}");
    }

    public void Dispose()
    {
        foreach (IDalamudTextureWrap? texture in _cache.Values)
            texture?.Dispose();
        _cache.Clear();
    }
}
