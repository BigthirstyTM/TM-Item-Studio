using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Managers;
using TmEssentials;

namespace TM_Item_Studio.Models;

/// <summary>A self-contained item whose root prefab holds one entry per placed copy of the same
/// model. The mesh is written once and referenced by every entry, so copies stay cheap.</summary>
public sealed record ItemMultiInstanceExport(
    byte[] Bytes,
    string FileName,
    string ItemId,
    int InstanceCount,
    int EntryCount,
    IReadOnlyList<string> Warnings);

/// <summary>Builds an item out of placed copies of one entity model.</summary>
/// <remarks>A macroblock cannot embed an item: its <c>ObjectSpawn</c> stores an <c>ident ItemModel</c>
/// reference only. A prefab entry however holds a node reference plus a rotation and a position,
/// and GBX.NET writes a repeated node once, so N copies of one mesh are stored as one mesh with N
/// transforms — the format-level equivalent of embedding the instances.</remarks>
public static class ItemMultiInstance
{
    private const float DegToRad = MathF.PI / 180f;

    /// <summary>Item ids must stay unique in the inventory, and files are named after the id, so the
    /// embedded copy gets its own id instead of shadowing the original item.</summary>
    public static string SuffixedId(string? baseId, int instanceCount)
    {
        var suffix = $"_x{Math.Max(1, instanceCount)}";
        var id = string.IsNullOrWhiteSpace(baseId) ? "CustomItem" : baseId.Trim();
        var maxBase = Math.Max(1, 64 - suffix.Length);
        return (id.Length > maxBase ? id[..maxBase] : id) + suffix;
    }

    /// <summary>Instances of the open item only: a macroblock can reference several items, but this
    /// export embeds one mesh, so copies of anything else cannot be represented.</summary>
    /// <param name="gbx">The document to embed. It is only read: the export works on its own
    /// snapshot, so the item the user is editing is never touched.</param>
    /// <param name="variantNumber">1-based variant to instance, matching
    /// <see cref="ItemVariantSource.VariantNumber"/>; ignored without a variant list.</param>
    public static ItemMultiInstanceExport Export(
        Gbx<CGameItemModel> gbx,
        int variantNumber,
        IReadOnlyList<ItemMacroblockInstance> instances,
        string? baseIdentId)
    {
        ArgumentNullException.ThrowIfNull(gbx);
        ArgumentNullException.ThrowIfNull(instances);
        if (instances.Count == 0)
            throw new InvalidOperationException("Add at least one copy before exporting a multi-instance item.");

        using var document = new MemoryStream();
        gbx.Save(document);
        using var snapshot = new MemoryStream(document.ToArray());
        var working = Gbx.Parse<CGameItemModel>(snapshot);
        var item = working.Node
            ?? throw new InvalidOperationException("The item could not be reopened for export.");

        NPlugItem_SVariant? activeVariant = item.EntityModel is NPlugItem_SVariantList { Variants.Length: > 0 } list
            ? list.Variants[Math.Clamp(variantNumber - 1, 0, list.Variants.Length - 1)]
            : null;
        var sourceRoot = activeVariant?.EntityModel ?? item.EntityModel
            ?? throw new InvalidOperationException("The item has no entity model to instance.");
        if (sourceRoot is NPlugItem_SVariantList)
            throw new InvalidOperationException(
                "Open a variant first: the export needs one concrete entity model to instance.");

        var itemIdentId = item.Ident?.Id;
        var effectiveId = !string.IsNullOrWhiteSpace(itemIdentId) ? itemIdentId : baseIdentId;
        var warnings = new List<string>();
        var usable = new List<ItemMacroblockInstance>(instances.Count);
        var foreign = 0;
        var scaled = 0;
        foreach (var instance in instances)
        {
            if (instance.ItemId is { Length: > 0 } referenced
                && !string.IsNullOrWhiteSpace(effectiveId)
                && !string.Equals(referenced, effectiveId, StringComparison.OrdinalIgnoreCase))
            {
                foreign++;
                continue;
            }

            usable.Add(instance);
            if (Math.Abs(instance.Scale - 1f) > 1e-3f)
                scaled++;
        }

        if (foreign > 0)
            warnings.Add($"{foreign} of {instances.Count} copies reference a different item and were skipped; " +
                         "only the open item can be embedded.");
        if (scaled > 0)
            warnings.Add($"{scaled} copies were placed with a scale; prefab entries carry no scale, so they were exported at scale 1.");
        if (usable.Count == 0)
            throw new InvalidOperationException(
                "None of the copies reference the open item, so there is nothing to embed.");

        // One prefab entry per source entry per copy: the source entries keep their own transform,
        // the copy's transform is applied on top of it.
        var sourcePrefab = sourceRoot as CPlugPrefab;
        var sourceEnts = sourcePrefab is { Ents.Length: > 0 } prefab ? prefab.Ents : Array.Empty<CPlugPrefab.EntRef>();
        var entriesPerInstance = Math.Max(1, sourceEnts.Length);
        var newEnts = new List<CPlugPrefab.EntRef>(usable.Count * entriesPerInstance);

        foreach (var instance in usable)
        {
            var placement = Quaternion.CreateFromYawPitchRoll(
                instance.YawDeg * DegToRad,
                instance.PitchDeg * DegToRad,
                instance.RollDeg * DegToRad);
            var origin = new Vector3(instance.PosX, instance.PosY, instance.PosZ);
            var placementQuat = new Quat(placement.X, placement.Y, placement.Z, placement.W);
            var placementPosition = new Vec3(origin.X, origin.Y, origin.Z);

            if (sourceEnts.Length == 0)
            {
                newEnts.Add(new CPlugPrefab.EntRef
                {
                    Model = sourceRoot,
                    Rotation = placementQuat,
                    Position = placementPosition,
                    Params = null,
                    U01 = ""
                });
                continue;
            }

            foreach (var source in sourceEnts)
            {
                var model = source.Model ?? source.GetModel()
                    ?? throw new InvalidOperationException(
                        "A prefab entry references an external model that cannot be embedded. " +
                        "Export the source file with its dependencies first.");

                var sourceRotation = new Quaternion(
                    source.Rotation.X, source.Rotation.Y, source.Rotation.Z, source.Rotation.W);
                var sourcePosition = new Vector3(
                    source.Position.X, source.Position.Y, source.Position.Z);

                // Row-vector order: the entry is placed first, the copy on top of it.
                var rotation = Matrix4x4.CreateFromQuaternion(sourceRotation)
                    * Matrix4x4.CreateFromQuaternion(placement);
                var combined = Quaternion.CreateFromRotationMatrix(rotation);
                var position = Vector3.Transform(sourcePosition, placement) + origin;

                newEnts.Add(new CPlugPrefab.EntRef
                {
                    Model = model,
                    Rotation = new Quat(combined.X, combined.Y, combined.Z, combined.W),
                    Position = new Vec3(position.X, position.Y, position.Z),
                    Params = source.Params,
                    U01 = source.U01
                });
            }
        }

        var instanced = new CPlugPrefab
        {
            Version = sourcePrefab?.Version ?? 11,
            Url = sourcePrefab?.Url ?? "",
            Ents = newEnts.ToArray()
        };

        // Keep the document's shape: an item that used a variant list still gets one (the active
        // variant's tags), a plain item still gets a plain entity model.
        item.EntityModel = activeVariant is null
            ? (CMwNod)instanced
            : new NPlugItem_SVariantList
            {
                Version = 1,
                Variants =
                [
                    new NPlugItem_SVariant
                    {
                        Tags = new Dictionary<string, string>(activeVariant.Tags),
                        HiddenInManualCycle = activeVariant.HiddenInManualCycle,
                        EntityModel = instanced
                    }
                ]
            };

        var id = SuffixedId(string.IsNullOrWhiteSpace(baseIdentId) ? itemIdentId : baseIdentId, usable.Count);
        // GBX.NET only writes chunks that exist on the node, and an item without a stored ident
        // has no ident chunk yet. This node is the export's own snapshot, so creating it is safe.
        ItemMacroblock.EnsureChunk(item, nameof(CGameItemModel.Ident));
        item.Ident = new Ident(
            id,
            item.Ident?.Collection ?? new Id("Stadium2020"),
            string.IsNullOrWhiteSpace(item.Ident?.Author) ? "TM_Item_Studio" : item.Ident!.Author);

        using var stream = new MemoryStream();
        working.Save(stream);
        var bytes = stream.ToArray();

        // Refuse to hand back a file the import parser cannot reopen, and prove the copies share
        // one model instead of carrying one mesh each.
        Verify(bytes, id, usable.Count, entriesPerInstance);

        return new ItemMultiInstanceExport(
            bytes, id + ".Item.Gbx", id, usable.Count, newEnts.Count, warnings);
    }

    public static void Verify(byte[] bytes, string expectedId, int expectedInstances, int entriesPerInstance)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var parsed = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes));
        var node = parsed.Node
            ?? throw new InvalidOperationException("The exported item has no readable node.");

        if (parsed.Header.ClassId != ClassManager.GetId(typeof(CGameItemModel)))
            throw new InvalidOperationException($"Unexpected item class id 0x{parsed.Header.ClassId:X8}.");
        if (!string.Equals(node.Ident?.Id, expectedId, StringComparison.Ordinal))
            throw new InvalidOperationException($"The export was written with id '{node.Ident?.Id}' instead of '{expectedId}'.");

        var root = node.EntityModel is NPlugItem_SVariantList { Variants.Length: > 0 } list
            ? list.Variants[0].EntityModel
            : node.EntityModel;
        if (root is not CPlugPrefab exported)
            throw new InvalidOperationException(
                $"The export root is {root?.GetType().Name ?? "null"} instead of a prefab.");

        var expectedEntries = expectedInstances * entriesPerInstance;
        if (exported.Ents.Length != expectedEntries)
            throw new InvalidOperationException(
                $"The export holds {exported.Ents.Length} entries instead of {expectedEntries}.");

        // Same node instance for the same source entry across copies: the writer stored the mesh once.
        if (expectedInstances > 1
            && !ReferenceEquals(exported.Ents[0].Model, exported.Ents[entriesPerInstance].Model))
            throw new InvalidOperationException(
                "Each copy carries its own model node; the mesh would be duplicated per copy.");
        if (expectedInstances > 1 && exported.Ents[0].Model is null)
            throw new InvalidOperationException("A prefab entry lost its model on export.");
    }
}
