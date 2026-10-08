using System.Numerics;
using System.Reflection;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Serialization.Chunking;

namespace TM_Item_Studio.Models;

/// <summary>Placement of one instanced copy of the loaded item inside a macroblock.
/// A macroblock spawn only stores the item ident plus a transform, so duplicating an item adds
/// a handful of bytes instead of a second copy of its geometry, icon or physics.</summary>
public sealed record ItemMacroblockInstance
{
    public string Label { get; set; } = "";

    /// <summary>Item reference read from a loaded spawn. Null means "the item that is open now",
    /// which is what freshly placed copies use.</summary>
    public string? ItemId { get; set; }

    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float PitchDeg { get; set; }
    public float YawDeg { get; set; }
    public float RollDeg { get; set; }
    public float Scale { get; set; } = 1f;

    public Vec3 Position => new(PosX, PosY, PosZ);

    /// <summary>Independent copy, used by the duplicate actions in the editor.</summary>
    public ItemMacroblockInstance Copy() => this with { };
}

public sealed record ItemMacroblockExport(byte[] Bytes, string FileName, int InstanceCount, string ItemId);

/// <summary>Builds <c>.Macroblock.Gbx</c> files (<c>CGameCtnMacroBlockInfo</c>) from a loaded item.
/// Only the item reference is written, never its mesh, so the file stays tiny as instances are added.</summary>
public static class ItemMacroblock
{
    /// <summary>Spawn payload version written to chunk 0x0310D00E. Version 14 is the newest
    /// object-spawn layout the bundled GBX.NET understands: it carries the full transform,
    /// scale and pack-descriptor flags without speculative fields beyond them.</summary>
    public const int SpawnVersion = 14;

    /// <summary>Class id of <c>.Macroblock.Gbx</c> files.</summary>
    public const uint MacroblockClassId = 0x0310D000;

    /// <summary>Chunk 0x0310D00E header version (annotated MP4.v2 / TM2020.v2 in the format description).</summary>
    public const int SpawnChunkVersion = 2;

    /// <summary>Coarse fallback grid written next to the exact position: one Trackmania block unit.
    /// <see cref="ItemMacroblockInstance"/> positions are meters; <c>AbsolutePositionInMap</c> is
    /// authoritative from spawn version 3 onwards.</summary>
    public const float BlockUnitSize = 32f;

    private const float DegToRad = MathF.PI / 180f;

    public static string DefaultName(CGameItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var baseName = string.IsNullOrWhiteSpace(item.Name) ? item.Ident?.Id : item.Name;
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "CustomItem";
        return baseName.Trim() + "_Macroblock";
    }

    /// <summary>Spreads <paramref name="count"/> copies over a rectangular grid on the X/Z plane.</summary>
    public static IReadOnlyList<ItemMacroblockInstance> Grid(int count, int columns, float spacingX, float spacingZ,
        float y = 0f, float yawDeg = 0f, float scale = 1f)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));

        var instances = new List<ItemMacroblockInstance>(count);
        for (var i = 0; i < count; i++)
        {
            instances.Add(new ItemMacroblockInstance
            {
                Label = $"Copy {i + 1}",
                PosX = (i % columns) * spacingX,
                PosY = y,
                PosZ = (i / columns) * spacingZ,
                YawDeg = yawDeg,
                Scale = scale
            });
        }

        return instances;
    }

    /// <summary>Raw header class id of a GBX file, or null when it cannot be read.</summary>
    public static uint? ClassIdOf(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 13) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return Gbx.ParseClassId(stream, remap: false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True when the bytes are a macroblock instead of an item, so the loader can
    /// dispatch instead of asking the item parser for a file it cannot cast.</summary>
    public static bool IsMacroblock(byte[] bytes) => ClassIdOf(bytes) == MacroblockClassId;

    /// <summary>Second-chance read for a file the item parser refused: GBX.NET answers a
    /// class-id mismatch with an <see cref="InvalidCastException"/>, which is what surfaced as
    /// "Specified cast is not valid." when a macroblock reached the item parser. Returns false
    /// only when the bytes are not a macroblock; a macroblock that fails to parse reports the
    /// parse failure through <paramref name="error"/>.</summary>
    public static bool TryRead(byte[] bytes, out CGameCtnMacroBlockInfo? node, out string? error)
    {
        node = null;
        error = null;
        if (bytes is null || bytes.Length < 13)
        {
            error = "The file is too small to be a GBX file.";
            return false;
        }

        var declaredMacroblock = IsMacroblock(bytes);
        try
        {
            node = Gbx.Parse<CGameCtnMacroBlockInfo>(new MemoryStream(bytes)).Node;
            return node is not null;
        }
        catch (InvalidCastException) when (!declaredMacroblock)
        {
            // The header says some other node type; this file simply is not a macroblock.
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>World placement of one copy: scale, then pitch/yaw/roll, then translation in
    /// meters. Applied after a part's own world transform it puts the copy exactly where the
    /// macroblock spawn places it, so the viewport can show instances of the loaded item.</summary>
    public static Matrix4x4 Placement(ItemMacroblockInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var scale = instance.Scale <= 0f ? 1f : instance.Scale;
        // Same yaw/pitch/roll order the studio already uses for composite world rotations.
        var rotation = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(
            instance.YawDeg * DegToRad,
            instance.PitchDeg * DegToRad,
            instance.RollDeg * DegToRad));
        return Matrix4x4.CreateScale(scale)
            * rotation
            * Matrix4x4.CreateTranslation(instance.PosX, instance.PosY, instance.PosZ);
    }

    /// <summary>Turns the spawns of a loaded macroblock into editable instances.</summary>
    public static IReadOnlyList<ItemMacroblockInstance> ReadInstances(CGameCtnMacroBlockInfo node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var spawns = node.ObjectSpawns ?? new List<CGameCtnMacroBlockInfo.ObjectSpawn>();
        var instances = new List<ItemMacroblockInstance>(spawns.Count);
        for (var i = 0; i < spawns.Count; i++)
        {
            var spawn = spawns[i];
            instances.Add(new ItemMacroblockInstance
            {
                Label = string.IsNullOrWhiteSpace(spawn.ItemModel?.Id) ? $"Copy {i + 1}" : spawn.ItemModel!.Id!,
                ItemId = string.IsNullOrWhiteSpace(spawn.ItemModel?.Id) ? null : spawn.ItemModel!.Id,
                PosX = spawn.AbsolutePositionInMap.X,
                PosY = spawn.AbsolutePositionInMap.Y,
                PosZ = spawn.AbsolutePositionInMap.Z,
                PitchDeg = MathF.Round(spawn.PitchYawRoll.X / DegToRad, 4),
                YawDeg = MathF.Round(spawn.PitchYawRoll.Y / DegToRad, 4),
                RollDeg = MathF.Round(spawn.PitchYawRoll.Z / DegToRad, 4),
                Scale = spawn.Scale <= 0f ? 1f : spawn.Scale
            });
        }

        return instances;
    }

    public static CGameCtnMacroBlockInfo Build(CGameItemModel item, IReadOnlyList<ItemMacroblockInstance> instances,
        string? macroblockName)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(instances);
        if (instances.Count == 0)
            throw new InvalidOperationException("Add at least one instance before exporting a macroblock.");

        var itemIdent = item.Ident;
        var itemId = itemIdent?.Id;
        if (string.IsNullOrWhiteSpace(itemId))
            throw new InvalidOperationException(
                "The item has no Ident.Id. Set it on the Identity tab: every macroblock instance references the item by that id.");

        // Each spawn reuses the same ident. No geometry, material, icon or physics node is written,
        // which is what keeps duplicated items from doubling the file size.
        var spawnIdent = new Ident(itemId, itemIdent!.Collection, itemIdent.Author);

        var name = string.IsNullOrWhiteSpace(macroblockName) ? DefaultName(item) : macroblockName.Trim();

        var node = new CGameCtnMacroBlockInfo
        {
            Name = name,
            Ident = new Ident(name, itemIdent.Collection, itemIdent.Author),
            Description = $"Exported by TM Item Studio: {instances.Count} instance(s) of {itemId}",
            BlockSpawns = new List<CGameCtnMacroBlockInfo.BlockSpawn>(),
            BlockSkinSpawns = new List<CGameCtnMacroBlockInfo.BlockSkinSpawn>(),
            CardEventsSpawns = new List<CGameCtnMacroBlockInfo.CardEventsSpawn>(),
            ObjectSpawns = new List<CGameCtnMacroBlockInfo.ObjectSpawn>()
        };

        // GBX.NET only serializes chunks that exist on the node, and a brand-new node has none.
        // Each written property therefore needs its owning chunk created first.
        EnsureChunk(node, nameof(CGameCtnMacroBlockInfo.Name));
        EnsureChunk(node, nameof(CGameCtnMacroBlockInfo.Ident));
        EnsureChunk(node, nameof(CGameCtnMacroBlockInfo.Description));
        EnsureChunk(node, nameof(CGameCtnMacroBlockInfo.BlockSpawns));
        EnsureChunk(node, nameof(CGameCtnMacroBlockInfo.BlockSkinSpawns));
        EnsureChunk(node, nameof(CGameCtnMacroBlockInfo.CardEventsSpawns));
        EnsureChunk(node, nameof(CGameCtnMacroBlockInfo.ObjectSpawns));

        if (node.Chunks.Get<CGameCtnMacroBlockInfo.Chunk0310D00E>() is { } spawnChunk)
            spawnChunk.Version = SpawnChunkVersion;

        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            if (instance.ItemId is { } referenced && !string.Equals(referenced, itemId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Instance {i + 1} references '{referenced}', but the loaded item is '{itemId}'. Open the matching item file before exporting, otherwise its reference would be rewritten.");
            node.ObjectSpawns.Add(BuildSpawn(spawnIdent, instance));
        }

        return node;
    }

    public static byte[] Save(CGameItemModel item, IReadOnlyList<ItemMacroblockInstance> instances, string? macroblockName)
    {
        var node = Build(item, instances, macroblockName);
        using var stream = new MemoryStream();
        new Gbx<CGameCtnMacroBlockInfo>(node).Save(stream);
        return stream.ToArray();
    }

    /// <summary>Builds, saves and reparses the macroblock with the same parser used for import,
    /// refusing to hand back bytes the reader cannot open again.</summary>
    public static ItemMacroblockExport Export(CGameItemModel item, IReadOnlyList<ItemMacroblockInstance> instances,
        string? macroblockName)
    {
        var bytes = Save(item, instances, macroblockName);
        Verify(bytes, item, instances);
        var name = string.IsNullOrWhiteSpace(macroblockName) ? DefaultName(item) : macroblockName.Trim();
        return new ItemMacroblockExport(bytes, name + ".Macroblock.Gbx", instances.Count, item.Ident!.Id!);
    }

    public static void Verify(byte[] bytes, CGameItemModel item, IReadOnlyList<ItemMacroblockInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var parsed = Gbx.Parse<CGameCtnMacroBlockInfo>(new MemoryStream(bytes));
        var node = parsed.Node
            ?? throw new InvalidOperationException("The exported macroblock has no readable node.");

        if (parsed.Header.ClassId != MacroblockClassId)
            throw new InvalidOperationException($"Unexpected macroblock class id 0x{parsed.Header.ClassId:X8}.");

        if (node.ObjectSpawns is not { } spawns || spawns.Count != instances.Count)
            throw new InvalidOperationException(
                $"Reparse found {node.ObjectSpawns?.Count ?? 0} instance(s) instead of {instances.Count}.");

        var itemId = item.Ident!.Id!;
        for (var i = 0; i < instances.Count; i++)
        {
            var spawn = spawns[i];
            var expected = instances[i];
            var expectedId = expected.ItemId ?? itemId;

            if (!string.Equals(spawn.ItemModel?.Id, expectedId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Instance {i + 1} references '{spawn.ItemModel?.Id}' instead of '{expectedId}'.");

            if (Math.Abs(spawn.AbsolutePositionInMap.X - expected.PosX) > 1e-3f
                || Math.Abs(spawn.AbsolutePositionInMap.Y - expected.PosY) > 1e-3f
                || Math.Abs(spawn.AbsolutePositionInMap.Z - expected.PosZ) > 1e-3f)
                throw new InvalidOperationException($"Instance {i + 1} position did not survive the round trip.");

            if (Math.Abs(spawn.PitchYawRoll.Y - expected.YawDeg * DegToRad) > 1e-4f)
                throw new InvalidOperationException($"Instance {i + 1} rotation did not survive the round trip.");

            if (spawn.Version != SpawnVersion)
                throw new InvalidOperationException($"Instance {i + 1} was written with spawn version {spawn.Version}.");

            if (spawn.Scale <= 0f)
                throw new InvalidOperationException($"Instance {i + 1} has an invalid scale of {spawn.Scale}.");
        }
    }

    private static CGameCtnMacroBlockInfo.ObjectSpawn BuildSpawn(Ident ident, ItemMacroblockInstance instance)
    {
        var position = new Vec3(instance.PosX, instance.PosY, instance.PosZ);
        var yawPitchRoll = new Vec3(instance.PitchDeg * DegToRad, instance.YawDeg * DegToRad, instance.RollDeg * DegToRad);
        var blockCoord = new Int3(
            (int)MathF.Floor(instance.PosX / BlockUnitSize),
            (int)MathF.Floor(instance.PosY / BlockUnitSize),
            (int)MathF.Floor(instance.PosZ / BlockUnitSize));

        return new CGameCtnMacroBlockInfo.ObjectSpawn
        {
            Version = SpawnVersion,
            ItemModel = ident,
            AdditionalDir = 0,
            PitchYawRoll = yawPitchRoll,
            BlockCoord = blockCoord,
            AnchorTreeId = "",
            AbsolutePositionInMap = position,
            // The spawn origin is authoritative; the pivot is reported at the placed origin so a
            // reader that prefers the pivot keeps the instance where the editor put it.
            PivotPosition = position,
            Scale = instance.Scale <= 0f ? 1f : instance.Scale,
            HasPackDesc = false,
            HasForegroundPackDesc = false
        };
    }

    /// <summary>Creates the chunk that owns <paramref name="propertyName"/> so GBX.NET writes it.
    /// The mapping comes from the property's <c>AppliedWithChunk</c> attribute, so it follows the
    /// bundled assembly instead of duplicating chunk ids here.</summary>
    public static void EnsureChunk(CMwNod node, string propertyName)
    {
        var property = FindProperty(node.GetType(), propertyName);
        if (property is null)
            throw new InvalidOperationException($"{propertyName} is not a property of {node.GetType().Name}.");

        var chunkTypes = property.GetCustomAttributesData()
            .Where(data => data.AttributeType.Name.StartsWith("AppliedWithChunk", StringComparison.Ordinal))
            .Select(data => data.AttributeType.IsGenericType ? data.AttributeType.GetGenericArguments()[0] : null)
            .Where(type => type is not null)
            .Cast<Type>()
            .ToArray();
        if (chunkTypes.Length == 0)
            throw new InvalidOperationException($"{propertyName} has no owning chunk in the bundled GBX.NET.");

        // Some properties are mirrored into a header chunk. The body writer skips those, so the
        // body chunk is the one that actually persists the value on export.
        var chunkType = chunkTypes.FirstOrDefault(type => !typeof(IHeaderChunk).IsAssignableFrom(type)) ?? chunkTypes[0];

        if (node.Chunks.Contains(chunkType))
            return;

        if (Activator.CreateInstance(chunkType) is not IChunk chunk)
            throw new InvalidOperationException($"Could not create chunk {chunkType.Name} for {propertyName}.");

        node.Chunks.Add(chunk);
    }

    private static PropertyInfo? FindProperty(Type type, string propertyName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property is not null) return property;
        }

        return null;
    }
}
