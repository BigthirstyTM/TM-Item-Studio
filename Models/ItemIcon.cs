using GBX.NET;
using GBX.NET.Engines.GameData;

namespace TM_Item_Studio.Models;

public static class ItemIcon
{
    // GBX.NET has already converted the raw GBX bottom-up rows to [x, y].
    public static (int[] Pixels, int Width, int Height) ReadRaw(Color[,] icon)
    {
        var width = icon.GetLength(0);
        var height = icon.GetLength(1);
        if (width is < 1 or > 1024 || height is < 1 or > 1024)
            throw new NotSupportedException($"Unsupported icon dimensions: {width}x{height}.");
        var pixels = new int[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var color = icon[x, y];
                var offset = (y * width + x) * 4;
                pixels[offset] = (int)Math.Clamp(color.R, 0, 255);
                pixels[offset + 1] = (int)Math.Clamp(color.G, 0, 255);
                pixels[offset + 2] = (int)Math.Clamp(color.B, 0, 255);
                pixels[offset + 3] = (int)Math.Clamp(color.A, 0, 255);
            }
        return (pixels, width, height);
    }

    public static void Replace(CGameCtnCollector item, int[] pixels, int width, int height, byte[] webp)
    {
        if (width is < 1 or > 1024 || height is < 1 or > 1024 || pixels.Length != width * height * 4 || webp.Length == 0)
            throw new ArgumentException("The decoded icon has invalid dimensions or pixel data.");
        var icon = new Color[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                icon[x, y] = new Color(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
            }
        // Without this header chunk an item that originally had no icon loses the replacement on save.
        item.CreateChunk<CGameCtnCollector.HeaderChunk2E001004>();
        item.Icon = icon;
        item.IconWebP = webp;
    }
}
