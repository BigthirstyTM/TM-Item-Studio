using System.Reflection;

namespace TM_Item_Studio.Models;

/// <summary>Loads the bundled Blender exporter without relying on a server-side file.</summary>
public static class BlenderAddonDownload
{
    public const string FileName = "trackmania_baked_cloth.zip";

    public static byte[] Read()
    {
        var assembly = typeof(BlenderAddonDownload).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".Resources.Blender." + FileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            throw new InvalidOperationException("The bundled Blender add-on resource is missing.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The bundled Blender add-on resource could not be opened.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (bytes.Length < 512)
            throw new InvalidDataException("The bundled Blender add-on archive is unexpectedly small.");
        return bytes;
    }
}





