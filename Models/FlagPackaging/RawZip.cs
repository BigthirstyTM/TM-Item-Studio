using System.Buffers.Binary;
using System.IO.Compression;

namespace TM_Item_Studio.Models.FlagPackaging;

// Bounded normal-ZIP splice for this export repair, not a general archive writer.
// Unchanged entries keep exact local records, compressed bytes, extra fields and comments.
internal sealed class RawZip
{
    internal sealed record Entry(string Name, byte[] Local, byte[] Central, int LocalOffset);
    public List<Entry> Entries { get; } = [];
    readonly byte[] end;
    public RawZip(byte[] bytes)
    {
        PackagingGuard.That(bytes.Length is >= 22 and <= PackagingGuard.MaxMapBytes, "ZIP exceeds the supported size limit or is truncated");
        var eocd = -1;
        for (var p = bytes.Length - 22; p >= Math.Max(0, bytes.Length - 65557); p--)
            if (U32(bytes, p) == 0x06054B50 && p + 22 + U16(bytes, p + 20) == bytes.Length) { eocd = p; break; }
        PackagingGuard.That(eocd >= 0, "Normal ZIP end record missing");
        PackagingGuard.That(U16(bytes, eocd + 4) == 0 && U16(bytes, eocd + 6) == 0, "Split ZIP is not supported");
        var count = U16(bytes, eocd + 10); var size = U32(bytes, eocd + 12); var cd = U32(bytes, eocd + 16);
        PackagingGuard.That(count is > 0 and <= 4096 && size != uint.MaxValue && cd != uint.MaxValue && U16(bytes, eocd + 8) == count, "ZIP64, excessive entry count or inconsistent ZIP is not supported");
        PackagingGuard.That((long)cd + size == eocd, "Unsupported data between ZIP directory and end record");
        end = bytes[eocd..];
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        PackagingGuard.That(archive.Entries.Count == count, "ZIP entry count disagreement");
        var rows = new List<(string Name, byte[] Central, int Offset)>(); var pos = checked((int)cd);
        for (var i = 0; i < count; i++)
        {
            PackagingGuard.That(U32(bytes, pos) == 0x02014B50, "ZIP central record missing");
            var length = checked(46 + U16(bytes, pos + 28) + U16(bytes, pos + 30) + U16(bytes, pos + 32));
            PackagingGuard.That(pos + length <= eocd && U16(bytes, pos + 34) == 0 && U32(bytes, pos + 20) != uint.MaxValue && U32(bytes, pos + 24) != uint.MaxValue && U32(bytes, pos + 42) != uint.MaxValue, "ZIP64, split or truncated entry is not supported");
            rows.Add((archive.Entries[i].FullName, bytes[pos..(pos + length)], checked((int)U32(bytes, pos + 42)))); pos += length;
        }
        PackagingGuard.That(pos == eocd, "Unexpected ZIP directory suffix");
        var offsets = rows.Select(r => r.Offset).Order().ToArray();
        PackagingGuard.That(offsets.Length > 0 && offsets[0] == 0 && offsets.Distinct().Count() == count, "Unsupported ZIP local layout");
        PackagingGuard.That(rows.Select(r => Canonical(r.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == count, "Duplicate or canonical-path-colliding ZIP names");
        foreach (var row in rows)
        {
            var next = offsets.FirstOrDefault(p => p > row.Offset, checked((int)cd));
            PackagingGuard.That(row.Offset >= 0 && row.Offset < next && next <= cd && (long)row.Offset + 30 <= next && U32(bytes, row.Offset) == 0x04034B50, "ZIP local record missing or overlapping");
            var flags = U16(row.Central, 8); var method = U16(row.Central, 10);
            PackagingGuard.That((flags & (1 | 0x40 | 0x2000)) == 0 && method is 0 or 8, "Encrypted or unsupported-compression ZIP is not supported");
            PackagingGuard.That(U16(bytes, row.Offset + 6) == flags && U16(bytes, row.Offset + 8) == method, "ZIP local/central flags or method disagree");
            var nameLength = U16(bytes, row.Offset + 26); var extraLength = U16(bytes, row.Offset + 28);
            var dataStart = checked(row.Offset + 30 + nameLength + extraLength);
            PackagingGuard.That(dataStart <= next && nameLength == U16(row.Central, 28)
                && bytes.AsSpan(row.Offset + 30, nameLength).SequenceEqual(row.Central.AsSpan(46, nameLength)), "ZIP local/central names or bounds disagree");
            Extras(bytes.AsSpan(row.Offset + 30 + nameLength, extraLength));
            Extras(row.Central.AsSpan(46 + nameLength, U16(row.Central, 30)));
            var dataEndLong = (long)dataStart + U32(row.Central, 20);
            PackagingGuard.That(dataEndLong <= next, "ZIP compressed payload overlaps another local entry");
            var dataEnd = checked((int)dataEndLong);
            if ((flags & 8) == 0)
                PackagingGuard.That(dataEnd == next && U32(bytes, row.Offset + 14) == U32(row.Central, 16)
                    && U32(bytes, row.Offset + 18) == U32(row.Central, 20) && U32(bytes, row.Offset + 22) == U32(row.Central, 24), "ZIP local CRC/size disagreement or unexplained local padding");
            else
            {
                var descriptor = dataEnd; var remaining = next - dataEnd;
                if (remaining == 16 && U32(bytes, descriptor) == 0x08074B50) descriptor += 4;
                else PackagingGuard.That(remaining == 12, "Unsupported ZIP data descriptor or unexplained padding");
                PackagingGuard.That(U32(bytes, descriptor) == U32(row.Central, 16) && U32(bytes, descriptor + 4) == U32(row.Central, 20)
                    && U32(bytes, descriptor + 8) == U32(row.Central, 24), "ZIP data descriptor CRC/size disagreement");
                PackagingGuard.That(U32(bytes, row.Offset + 18) != uint.MaxValue && U32(bytes, row.Offset + 22) != uint.MaxValue, "ZIP64 local entry is not supported");
            }
            Entries.Add(new(row.Name, bytes[row.Offset..next], row.Central, row.Offset));
        }
    }
    public byte[] Replace(IReadOnlyDictionary<string, byte[]> replacements, IReadOnlyDictionary<string, byte[]> additions)
    {
        PackagingGuard.That(replacements.Keys.All(k => Entries.Any(e => e.Name == k)), "Replacement names an absent ZIP entry");
        PackagingGuard.That(additions.Keys.All(k => Entries.All(e => !Canonical(e.Name).Equals(Canonical(k), StringComparison.OrdinalIgnoreCase))), "Addition collides with a ZIP entry");
        var changes = new Dictionary<string, Entry>();
        foreach (var pair in replacements.Concat(additions))
        {
            var previous = Entries.FirstOrDefault(e => e.Name == pair.Key);
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                var created = zip.CreateEntry(pair.Key, CompressionLevel.Optimal);
                if (previous is not null) { created.ExternalAttributes = I32(previous.Central, 38); created.LastWriteTime = DosTime(previous.Central); }
                else created.LastWriteTime = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
                using var stream = created.Open(); stream.Write(pair.Value);
            }
            changes.Add(pair.Key, new RawZip(memory.ToArray()).Entries.Single());
        }
        using var result = new MemoryStream(); var offsets = new Dictionary<string, int>();
        foreach (var entry in Entries.OrderBy(e => e.LocalOffset))
        {
            offsets[entry.Name] = checked((int)result.Position);
            result.Write(changes.TryGetValue(entry.Name, out var changed) ? changed.Local : entry.Local);
        }
        foreach (var pair in additions) { offsets[pair.Key] = checked((int)result.Position); result.Write(changes[pair.Key].Local); }
        var cdStart = checked((int)result.Position);
        foreach (var entry in Entries.Concat(additions.Select(p => changes[p.Key])))
        {
            var record = (changes.TryGetValue(entry.Name, out var changed) ? changed.Central : entry.Central).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(42), checked((uint)offsets[entry.Name])); result.Write(record);
        }
        var cdSize = checked((int)result.Position - cdStart); var trailer = end.ToArray(); var total = checked(Entries.Count + additions.Count);
        PackagingGuard.That(total < ushort.MaxValue, "ZIP entry count exceeds bounded writer");
        BinaryPrimitives.WriteUInt16LittleEndian(trailer.AsSpan(8), (ushort)total); BinaryPrimitives.WriteUInt16LittleEndian(trailer.AsSpan(10), (ushort)total);
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(12), (uint)cdSize); BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(16), (uint)cdStart); result.Write(trailer);
        var output = result.ToArray(); var verified = new RawZip(output);
        foreach (var old in Entries.Where(e => !replacements.ContainsKey(e.Name)))
        {
            var next = verified.Entries.Single(e => e.Name == old.Name);
            PackagingGuard.That(old.Local.AsSpan().SequenceEqual(next.Local), "An unrelated ZIP local record or compressed payload changed");
            var oldCentral = old.Central.ToArray(); var newCentral = next.Central.ToArray(); oldCentral.AsSpan(42, 4).Clear(); newCentral.AsSpan(42, 4).Clear();
            PackagingGuard.That(oldCentral.AsSpan().SequenceEqual(newCentral), "Unrelated ZIP central metadata changed");
        }
        return output;
    }
    static DateTimeOffset DosTime(byte[] central)
    {
        var time = U16(central, 12); var date = U16(central, 14);
        PackagingGuard.That(((date >> 5) & 15) is >= 1 and <= 12 && (date & 31) is >= 1 and <= 31 && (time >> 11) < 24 && ((time >> 5) & 63) < 60 && (time & 31) < 30, "Unsupported ZIP DOS timestamp");
        return new DateTimeOffset(1980 + (date >> 9), (date >> 5) & 15, date & 31, time >> 11, (time >> 5) & 63, (time & 31) * 2, TimeSpan.Zero);
    }
    static string Canonical(string name)
    {
        return PackagingGuard.CanonicalPath(name);
    }
    static void Extras(ReadOnlySpan<byte> bytes)
    {
        for (var p = 0; p < bytes.Length;)
        {
            PackagingGuard.That(p + 4 <= bytes.Length, "Truncated ZIP extra field");
            var id = BinaryPrimitives.ReadUInt16LittleEndian(bytes[p..]); var size = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(p + 2)..]);
            PackagingGuard.That(id != 1 && (long)p + 4 + size <= bytes.Length, "ZIP64 or truncated ZIP extra field is not supported"); p += 4 + size;
        }
    }
    static ushort U16(byte[] b, int p) { PackagingGuard.That(p >= 0 && p + 2 <= b.Length, "Truncated ZIP integer"); return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p)); }
    static uint U32(byte[] b, int p) { PackagingGuard.That(p >= 0 && p + 4 <= b.Length, "Truncated ZIP integer"); return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p)); }
    static int I32(byte[] b, int p) => unchecked((int)U32(b, p));
}





