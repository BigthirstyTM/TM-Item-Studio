using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using System.Text;

namespace TM_Item_Studio.Models;

public static class ItemExportValidator
{
    public static void EnsureGameReady(CGameItemModel item, string? preferredId = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.EntityModel is null)
            throw new InvalidOperationException("The item has no entity model and cannot be loaded by Trackmania.");

        var normalizedId = NormalizeItemId(item.Ident?.Id);
        if (string.IsNullOrWhiteSpace(normalizedId))
            normalizedId = NormalizeItemId(preferredId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            normalizedId = NormalizeItemId(item.Name);
        if (string.IsNullOrWhiteSpace(normalizedId))
            normalizedId = "CustomItem";

        var collection = item.Ident?.Collection ?? new Id("Stadium2020");
        var author = string.IsNullOrWhiteSpace(item.Ident?.Author) ? "TM_Item_Studio" : item.Ident!.Author;
        item.Ident = new Ident(normalizedId, collection, author);
        if (string.IsNullOrWhiteSpace(item.Name))
            item.Name = normalizedId;
        if (string.IsNullOrWhiteSpace(item.ArchetypeRef))
            item.ArchetypeRef = normalizedId;
        if (string.IsNullOrWhiteSpace(item.PageName))
            item.PageName = "Items";
        if (item.CatalogPosition <= 0)
            item.CatalogPosition = 1;

        if (item.EntityModel is CPlugPrefab prefab)
            NormalizePrefabTransforms(prefab, "root/entityModel");
    }

    private static string NormalizeItemId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var source = raw.Trim();
        var sb = new StringBuilder(source.Length);
        foreach (var ch in source)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
        var value = sb.ToString().Trim('_');
        if (value.Length == 0)
            return "";
        if (char.IsDigit(value[0]))
            value = "Item_" + value;
        return value.Length > 64 ? value[..64] : value;
    }

    private static void NormalizePrefabTransforms(CPlugPrefab prefab, string path)
    {
        if (prefab.Ents is null)
            return;

        for (var i = 0; i < prefab.Ents.Length; i++)
        {
            var entry = prefab.Ents[i];
            if (!IsFinite(entry.Position))
                throw new InvalidOperationException($"The merged prefab contains non-finite position values at {path}/ent:{i}.");
            entry.Rotation = NormalizeRotation(entry.Rotation, $"{path}/ent:{i}");
            if (entry.Model is CPlugPrefab nested)
                NormalizePrefabTransforms(nested, $"{path}/ent:{i}");
        }
    }

    private static bool IsFinite(Vec3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static Quat NormalizeRotation(Quat rotation, string path)
    {
        if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z) || !float.IsFinite(rotation.W))
            throw new InvalidOperationException($"The merged prefab contains non-finite rotation values at {path}.");

        var length = MathF.Sqrt(rotation.X * rotation.X + rotation.Y * rotation.Y + rotation.Z * rotation.Z + rotation.W * rotation.W);
        if (!float.IsFinite(length) || length < 1e-5f)
            return new Quat(0, 0, 0, 1);

        var inv = 1f / length;
        return new Quat(rotation.X * inv, rotation.Y * inv, rotation.Z * inv, rotation.W * inv);
    }
}
