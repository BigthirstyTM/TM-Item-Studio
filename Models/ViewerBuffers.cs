using System.Runtime.InteropServices;

namespace TM_Item_Studio.Models;

/// <summary>Little-endian 32-bit geometry words. Blazor transfers byte[] directly,
/// avoiding per-number JSON formatting for large vertex and index buffers.</summary>
public static class ViewerBuffers
{
    public static byte[] Pack(float[] values) => LittleEndian(MemoryMarshal.AsBytes(values.AsSpan()));
    public static byte[] Pack(int[] values) => LittleEndian(MemoryMarshal.AsBytes(values.AsSpan()));

    private static byte[] LittleEndian(ReadOnlySpan<byte> words)
    {
        var bytes = words.ToArray();
        if (!BitConverter.IsLittleEndian)
            for (var i = 0; i < bytes.Length; i += 4) Array.Reverse(bytes, i, 4);
        return bytes;
    }
}
