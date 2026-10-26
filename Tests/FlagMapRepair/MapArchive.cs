using System.Buffers.Binary;
using GBX.NET;
using GBX.NET.Engines.Game;

internal sealed class MapArchive
{
    public byte[] Source { get; }
    public byte[] Header { get; }
    public byte[] Body { get; }
    public CGameCtnChallenge Map { get; }
    public MapArchive(byte[] source)
    {
        Source = source;
        Check.That(source.Length > 25 && source.AsSpan(0, 7).SequenceEqual("GBX\x06\0BU"u8) && source[7] is (byte)'C' or (byte)'U', "Only binary GBX6 maps with uncompressed reference tables are supported");
        using var stream = new MemoryStream(source);
        var header = Gbx.ParseHeader<CGameCtnChallenge>(stream);
        Check.That(header.Header.ClassId == 0x03043000, "Input is not a Trackmania map");
        // ParseHeader also consumes the two compressed-body size words, but no compressed bytes.
        var bodyOffset = checked((int)stream.Position - (source[7] == (byte)'C' ? 8 : 0)); Header = source[..bodyOffset];
        if (source[7] == (byte)'C')
        {
            var rawSize = Int(source, bodyOffset); var compressedSize = Int(source, bodyOffset + 4);
            Check.That(rawSize is > 0 and <= 268435456 && compressedSize > 0 && (long)bodyOffset + 8 + compressedSize == source.Length, "Invalid or trailing compressed map body");
            Body = new byte[rawSize]; Gbx.LZO!.Decompress(source[(bodyOffset + 8)..], Body);
        }
        else { Check.That(source.Length - bodyOffset is > 0 and <= 268435456, "Uncompressed map body exceeds bounded repair"); Body = source[bodyOffset..]; }
        Map = Gbx.Parse<CGameCtnChallenge>(new MemoryStream(source), new GbxReadSettings { SkipChunkIds = [0x0304305B] }).Node;
    }
    public (byte[] Output, object Evidence) ReplaceZip(byte[] replacement)
    {
        var zip = Map.EmbeddedZipData!; var matches = new List<(int Start, int End, int Zip)>();
        ReadOnlySpan<byte> signature = [0x54, 0x30, 0x04, 0x03, 0x50, 0x49, 0x4B, 0x53];
        for (var scan = 0; scan <= Body.Length - 24;)
        {
            var relative = Body.AsSpan(scan).IndexOf(signature); if (relative < 0) break;
            var start = scan + relative; scan = start + 1;
            if (start + 24 > Body.Length) continue;
            var size = Int(Body, start + 8); var version = Int(Body, start + 12); var enclosed = Int(Body, start + 20);
            if (size < 12 || (long)start + 12 + size > Body.Length || version is < 0 or > 1 || Int(Body, start + 16) != 0 || enclosed != size - 12) continue;
            var end = start + 12 + size; var relativeZip = Body.AsSpan(start + 24, end - start - 24).IndexOf(zip);
            if (relativeZip < 4) continue; var position = start + 24 + relativeZip;
            if (Int(Body, position - 4) != zip.Length || Body.AsSpan(position + 1, end - position - 1).IndexOf(zip) >= 0) continue;
            matches.Add((start, end, position));
        }
        Check.That(matches.Count == 1, "Cannot uniquely locate the bounded embedded-files chunk and exact source ZIP");
        var chunk = matches.Single();
        var patched = Body[..(chunk.Zip - 4)].Concat(BitConverter.GetBytes(replacement.Length)).Concat(replacement).Concat(Body[(chunk.Zip + zip.Length)..]).ToArray();
        var delta = replacement.Length - zip.Length;
        Put(patched, chunk.Start + 8, checked(Int(Body, chunk.Start + 8) + delta));
        Put(patched, chunk.Start + 20, checked(Int(Body, chunk.Start + 20) + delta));
        var restored = patched[..(chunk.Zip - 4)].Concat(BitConverter.GetBytes(zip.Length)).Concat(zip).Concat(patched[(chunk.Zip + replacement.Length)..]).ToArray();
        Put(restored, chunk.Start + 8, Int(Body, chunk.Start + 8)); Put(restored, chunk.Start + 20, Int(Body, chunk.Start + 20));
        Check.That(restored.AsSpan().SequenceEqual(Body), "Original entire uncompressed map body is not exactly restorable");
        using var output = new MemoryStream(); output.Write(Header);
        if (Source[7] == (byte)'C') { var compressed = Gbx.LZO!.Compress(patched); output.Write(BitConverter.GetBytes(patched.Length)); output.Write(BitConverter.GetBytes(compressed.Length)); output.Write(compressed); }
        else output.Write(patched);
        var result = output.ToArray(); var check = new MapArchive(result);
        Check.That(check.Header.AsSpan().SequenceEqual(Header) && check.Body.AsSpan().SequenceEqual(patched) && check.Map.EmbeddedZipData!.AsSpan().SequenceEqual(replacement), "Reparsed output does not match exact raw splice");
        return (result, new { originalHeaderIncludingMapRefTableBytes = Header.Length, originalHeaderSha256 = Check.Hash(Header), originalHeaderByteExact = true,
            originalUncompressedBodySha256 = Check.Hash(Body), outputUncompressedBodySha256 = Check.Hash(patched),
            embeddedChunkOffsetInUncompressedBody = chunk.Start, zipOffsetInUncompressedBody = chunk.Zip,
            changedLengthWordOffsets = new[] { chunk.Start + 8, chunk.Start + 20, chunk.Zip - 4 },
            entireOriginalUncompressedBodyRestorableExactly = true,
            nonZipUncompressedBodyBytesUnchangedExceptThreeLengthWords = true, mapNodeSerializationPerformed = false,
            compressionModeRetained = (char)Source[7] });
    }
    static int Int(byte[] bytes, int offset) { Check.That(offset >= 0 && offset + 4 <= bytes.Length, "Truncated map integer"); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)); }
    static void Put(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
}
