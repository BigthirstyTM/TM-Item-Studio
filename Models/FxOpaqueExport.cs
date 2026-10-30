using System.Runtime.CompilerServices;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;

namespace TM_Item_Studio.Models;

/// <summary>
/// Fresh-document metadata export followed by a validated native FX offset
/// splice. The diagnostic JSON is optional and is not consumed by the game.
/// </summary>
public static class FxOpaqueExport
{
    public const uint FxSystemClassId = 0x0915C000;
    static readonly ConditionalWeakTable<Gbx<CGameItemModel>, SourceSnapshot> Sources = new();
    public sealed record EmissionPoint(float X, float Y, float Z);

    public sealed record Result(byte[] ItemBytes, string ItemFileName, byte[] SettingsBytes,
        string SettingsFileName, int OpaqueBodyBytes, string OpaqueBodySha256,
        // Compatibility name: bytes outside validated metadata strings and
        // emission expressions remain preserved.
        bool BodyPreserved, bool NativeOffsetWritten)
    {
        // The chosen clone/download name is distinct from a native null ID,
        // which lets the engine resolve the item identity from its actual FID.
        public string CloneId { get; init; } = string.Empty;
        // Install directly under the game's Items directory. A non-null native
        // ID must describe the same Items-relative path as the download name.
        public string ItemRelativePath { get; init; } = string.Empty;
        public string? HeaderId { get; init; }
        public string? BodyId { get; init; }
        public bool NativeNullIdentityRetained { get; init; }
    }

    sealed record SourceSnapshot(byte[] Bytes, FxEmitterTransform.Inspection? Inspection);

    public static bool IsOpaqueFxCarrier(Gbx<CGameItemModel>? document)
        => document?.Node is { EntityModel: null, ItemType: CGameItemModel.EItemType.Ornament }
            && Snapshot(document)?.Inspection is not null;

    public static bool TryReadEmissionPoint(Gbx<CGameItemModel>? document, out EmissionPoint? point)
    {
        var native = IsOpaqueFxCarrier(document) ? Snapshot(document!)?.Inspection?.Primary : null;
        point = native is null ? null : new(native.X, native.Y, native.Z);
        return point is not null;
    }

    public static Gbx<CGameItemModel> Parse(ReadOnlyMemory<byte> bytes)
    {
        var original = bytes.ToArray();
        var document = Gbx.Parse<CGameItemModel>(new MemoryStream(original, writable: false),
            new GbxReadSettings { SafeSkippableChunks = true, IgnoreExceptionsInBody = true, ReadRawBody = true });
        FxEmitterTransform.TryRead(original, out var inspection, out _);
        Sources.Add(document, new(original, inspection));
        return document;
    }

    static SourceSnapshot? Snapshot(Gbx<CGameItemModel> document)
    {
        if (Sources.TryGetValue(document, out var source)) return source;
        if (document.Body?.RawData.IsDefaultOrEmpty != false) return null;
        try
        {
            using var stream = new MemoryStream(); document.Save(stream);
            var bytes = stream.ToArray();
            FxEmitterTransform.TryRead(bytes, out var inspection, out _);
            return Sources.GetValue(document, _ => new(bytes, inspection));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    public static Result Export(Gbx<CGameItemModel> source, string preferredId,
        string? displayName, string? author, EmissionPoint emissionPoint)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(emissionPoint);
        if (!IsOpaqueFxCarrier(source) || Snapshot(source) is not { Inspection: not null } snapshot)
            throw new InvalidOperationException("The selected item has no supported embedded FX graph with constant native emission points.");

        // Export starts from the retained source bytes. Repeated exports and
        // failed operations never mutate the document displayed by Studio.
        var fresh = Parse(snapshot.Bytes);
        var item = fresh.Node;
        var id = NormalizeId(preferredId, item.Name);
        var itemRelativePath = SanitizeFileStem(id) + ".Item.Gbx";
        var retainNullIdentity = string.IsNullOrEmpty(item.Ident?.Id)
            && FxIdentity.Read(snapshot.Bytes).Id is null;
        var headerId = retainNullIdentity ? null : itemRelativePath;
        var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
        var resolvedAuthor = string.IsNullOrWhiteSpace(author) ? "TM_Item_Studio" : author.Trim();
        // GBX.NET serializes an empty Ident.Id as the native 0xFFFFFFFF null
        // tag. Defining a header ID for a native null-ID carrier changes its
        // engine identity policy even when the opaque body remains untouched.
        item.Ident = new Ident(headerId ?? string.Empty,
            item.Ident?.Collection ?? new Id("Stadium2020"), resolvedAuthor);
        item.Name = name;
        item.Description = name;
        if (string.IsNullOrWhiteSpace(item.PageName)) item.PageName = "Items";
        if (item.CatalogPosition <= 0) item.CatalogPosition = 1;
        using var metadataStream = new MemoryStream(); fresh.Save(metadataStream);
        var metadataBytes = metadataStream.ToArray();
        if (!FxEmissionPoint.BodiesEqual(snapshot.Bytes, metadataBytes))
            throw new InvalidOperationException("Metadata serialization changed the opaque FX payload; no item was exported.");

        // Replace an existing body definition in its original lookback slot.
        // FxIdentity retains an original null body ID instead of adding a slot.
        var identity = FxIdentity.Export(metadataBytes, itemRelativePath, resolvedAuthor, name, name);
        var native = FxEmitterTransform.Export(identity.ItemBytes,
            new(emissionPoint.X, emissionPoint.Y, emissionPoint.Z));
        var reparsed = Parse(native.ItemBytes);
        var actualHeaderId = NullIfEmpty(reparsed.Node.Ident?.Id);
        if (!TryReadEmissionPoint(reparsed, out var actual)
            || !Close(actual!, emissionPoint) || actualHeaderId != headerId
            || reparsed.Node.Ident?.Author != resolvedAuthor || reparsed.Node.Name != name)
            throw new InvalidOperationException("The exported identity, name or native emission point did not reparse correctly.");
        var proof = native.Proof;
        var settings = new
        {
            schemaVersion = 4, generatedAtUtc = DateTimeOffset.UtcNow,
            diagnosticOnly = true, requiredForGame = false,
            carrier = "embedded CPlugFxSystem", classId = $"0x{FxSystemClassId:X8}",
            cloneId = id, itemId = actualHeaderId, headerId = actualHeaderId,
            bodyId = identity.After.Id, author = resolvedAuthor,
            itemRelativePath, installAt = "Items/" + itemRelativePath,
            itemType = reparsed.Node.ItemType.ToString(),
            emissionPoint = actual, emissionPointSpace = "item-local metres", coordinateSpace = "item",
            emissionPointNativeField = "CPlugFxSystem ParticleEmitter LocalOffsetExpr",
            nativeOffsetWritten = true,
            emitterCount = native.After.Emitters.Count,
            itemEmitterPositions = native.After.Emitters,
            nativeFxLocalEmitterPositions = native.After.Native.Emitters.Select(emitter => emitter.Position),
            fxOwnerTransform = native.After.Owner,
            opaqueBodyBytes = proof.OutputBodyBytes, opaqueBodySha256 = proof.OutputBodySha256,
            bodyPreserved = true,
            bodyPreservedMeaning = "All original body bytes outside validated existing identity, author, name, description and emission expression strings, including their length words, are preserved.",
            metadataSaveBodyByteExact = true,
            metadataIdentityConsistent = true,
            nativeIdentityWritten = true,
            nativeNullIdentityRetained = retainNullIdentity,
            bodyIdUsesFileId = identity.After.Id is null,
            nativeIdentityProof = identity.Proof,
            nativeEditProof = proof,
            verification = "Offline native-field write and reparse; particle rendering, dependencies and standalone playback require separate in-game verification.",
            standaloneRuntimeVerified = false
        };
        var settingsBytes = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions
        { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var stem = SanitizeFileStem(id);
        return new(native.ItemBytes, itemRelativePath, settingsBytes, stem + ".fxsettings.json",
            proof.OutputBodyBytes, proof.OutputBodySha256, true, true)
        {
            CloneId = id, ItemRelativePath = itemRelativePath,
            HeaderId = actualHeaderId, BodyId = identity.After.Id,
            NativeNullIdentityRetained = retainNullIdentity
        };
    }

    static string NormalizeId(string? preferred, string? fallback)
    {
        var source = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
        var chars = (source ?? "CustomFx").Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray();
        var normalized = new string(chars).Trim('_');
        if (normalized.Length == 0) normalized = "CustomFx";
        if (char.IsDigit(normalized[0])) normalized = "Item_" + normalized;
        return normalized.Length > 64 ? normalized[..64] : normalized;
    }
    static string SanitizeFileStem(string value) => value.Replace('\\', '_').Replace('/', '_');
    static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    static bool Close(EmissionPoint a, EmissionPoint b)
        => Math.Abs(a.X - b.X) <= .001f && Math.Abs(a.Y - b.Y) <= .001f && Math.Abs(a.Z - b.Z) <= .001f;
}





