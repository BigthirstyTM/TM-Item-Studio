using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace TM_Item_Studio.Models.FlagPackaging;

internal static class PackagingGuard
{
    internal const int MaxFileBytes = 134217728;
    internal const int MaxMapBytes = 268435456;
    internal static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string CanonicalPath(string name)
    {
        var path = name.Replace('\\', '/');
        var parts = path.TrimEnd('/').Split('/');
        That(path.Length is > 0 and <= 1024 && !path.StartsWith('/') && !path.Contains(':')
            && !path.Any(char.IsControl) && parts.All(p => p is not ("" or "." or "..")
                && p.Length <= 255 && !p.EndsWith(' ') && !p.EndsWith('.')),
            "Unsupported or ambiguous ZIP path");
        return string.Join('/', parts);
    }
    internal static Dictionary<string, byte[]> ReadZip(byte[] bytes)
    {
        That(bytes.Length is > 0 and <= MaxMapBytes, "ZIP exceeds the supported size limit");
        _ = new RawZip(bytes);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        That(zip.Entries.Count is > 0 and <= 4096, "ZIP entry count exceeds the supported limit");
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var canonical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            That(canonical.Add(CanonicalPath(entry.FullName)), "Duplicate or canonical-path-colliding ZIP names");
            That(entry.Length is >= 0 and <= MaxFileBytes && (total += entry.Length) <= MaxMapBytes,
                "Embedded payload size exceeds the supported limit");
            using var input = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            for (var read = input.Read(buffer); read != 0; read = input.Read(buffer))
            {
                That(output.Length + read <= entry.Length, "ZIP payload expands beyond its declared size");
                output.Write(buffer, 0, read);
            }
            var data = output.ToArray();
            That(data.Length == entry.Length && Crc32.HashToUInt32(data) == entry.Crc32,
                "ZIP payload length or CRC does not match its record");
            result.Add(entry.FullName, data);
        }
        return result;
    }
}





