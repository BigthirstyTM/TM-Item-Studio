using System.Buffers.Binary;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;

namespace TM_Item_Studio.Models;

/// <summary>
/// Recovers a supported FX carrier's original mesh for preview. A temporary
/// parse copy omits the already validated FX archive because GBX.NET cannot
/// read it. Neither this copy nor its typed nodes may be used for item export.
/// </summary>
public static class FxMeshPreview
{
    const int MaxBodyBytes = 268435456;
    public sealed record Evidence(bool OriginalBodyRestorableExactly,
        bool MeshPrefixPreservedExactly, int RemovedFxBytes, int PrefabEntries,
        int StaticObjects, bool PreviewOnly = true);
    public sealed record Result(CPlugPrefab PreviewRoot, Evidence Proof);

    public static bool TryRead(ReadOnlyMemory<byte> itemBytes, out Result? result, out string? error)
    {
        try { result = Read(itemBytes); error = null; return true; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { result = null; error = exception.Message; return false; }
    }

    public static Result Read(ReadOnlyMemory<byte> itemBytes)
    {
        // Reuse the complete bounded graph validation; no FACADE-only scan.
        var inspection = FxEmissionPoint.Read(itemBytes);
        var source = itemBytes.ToArray();
        using var stream = new MemoryStream(source, writable: false);
        Gbx.ParseHeader<CGameItemModel>(stream);
        var prefixLength = checked((int)stream.Position - (source[7] == 'C' ? 8 : 0));
        Require(prefixLength >= 25 && prefixLength < source.Length, "Invalid FX preview body boundary.");
        byte[] body;
        if (source[7] == 'C')
        {
            var size = UInt(source, prefixLength);
            Require(size is > 0 and <= MaxBodyBytes && Gbx.LZO is not null, "Unsupported compressed FX preview body.");
            body = new byte[checked((int)size)];
            Gbx.LZO!.Decompress(source[(prefixLength + 8)..], body);
        }
        else body = source[prefixLength..];

        var end = inspection.Emitters[^1].LengthOffset;
        for (var field = 0; field < 10; field++) SkipString(body, ref end);
        Require(UInt(body, end) <= 1, "Invalid FX emitter boolean."); end += 4;
        SkipString(body, ref end); SkipString(body, ref end);
        uint[] trailer = [uint.MaxValue, uint.MaxValue, 0, 55, 0xFACADE01];
        foreach (var value in trailer)
        { Require(UInt(body, end) == value, "Unsupported FX graph trailer."); end += 4; }
        var start = inspection.FxGraphOffset - 4; // include its embedded node reference
        Require(start >= 0 && end > start + 4 && end <= body.Length - 36,
            "FX preview range does not leave a complete prefab owner record.");

        var stripped = new byte[checked(body.Length - (end - start) + 4)];
        body.AsSpan(0, start).CopyTo(stripped);
        BinaryPrimitives.WriteUInt32LittleEndian(stripped.AsSpan(start), uint.MaxValue);
        body.AsSpan(end).CopyTo(stripped.AsSpan(start + 4));
        // Reverse the precise splice before allowing any preview to escape.
        var restored = new byte[body.Length];
        stripped.AsSpan(0, start).CopyTo(restored);
        body.AsSpan(start, end - start).CopyTo(restored.AsSpan(start));
        stripped.AsSpan(start + 4).CopyTo(restored.AsSpan(end));
        Require(restored.AsSpan().SequenceEqual(body), "FX preview splice is not exactly reversible.");

        // Keep native node indices and the original reference table. Parsing an
        // uncompressed temporary copy does not reserialize the original mesh.
        var copy = new byte[checked(prefixLength + stripped.Length)];
        source.AsSpan(0, prefixLength).CopyTo(copy); copy[7] = (byte)'U';
        stripped.CopyTo(copy, prefixLength);
        var document = Gbx.Parse<CGameItemModel>(new MemoryStream(copy, writable: false),
            new GbxReadSettings { SafeSkippableChunks = true });
        Require(document.Node.EntityModel is CPlugPrefab, "The FX mesh preview must be an embedded prefab.");
        var prefab = (CPlugPrefab)document.Node.EntityModel!;
        // This preview schema is deliberately limited to the validated carrier:
        // one actual static mesh and one FX owner, with no invented shell.
        Require(prefab.Ents is { Length: 2 }
            && prefab.Ents[0].Model is CPlugStaticObjectModel
            && prefab.Ents[1].Model is null,
            "Only a two-entry static-mesh plus FX prefab can use the bounded mesh preview.");
        return new(prefab, new(true, body.AsSpan(0, start).SequenceEqual(stripped.AsSpan(0, start)),
            end - start, prefab.Ents.Length, 1));
    }

    static void SkipString(byte[] bytes, ref int position)
    {
        var length = UInt(bytes, position); position += 4;
        Require(length <= 2048 && (long)position + length <= bytes.Length, "Invalid FX preview expression length.");
        position += checked((int)length);
    }
    static uint UInt(byte[] bytes, int position)
    {
        Require(position >= 0 && position <= bytes.Length - 4, "Truncated FX preview archive.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
