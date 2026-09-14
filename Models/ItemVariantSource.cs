using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.MwFoundations;

namespace TM_Item_Studio.Models;

/// <summary>A view into a document. Selecting a view never replaces the document's root.</summary>
public sealed class ItemVariantSource
{
    public string Name { get; }
    public string FileName { get; }
    public int VariantNumber { get; }
    public Gbx<CGameItemModel> GbxFile { get; }
    public CGameItemModel Model => GbxFile.Node;
    public NPlugItem_SVariant? Variant { get; }
    public CMwNod? PreviewRoot => Variant is null ? Model : Variant.EntityModel;
    public string? SerializationWarning { get; }

    private ItemVariantSource(string name, Gbx<CGameItemModel> file, NPlugItem_SVariant? variant, int? variantNumber, string? serializationWarning)
    {
        FileName = name;
        VariantNumber = variantNumber ?? 1;
        Name = variantNumber.HasValue ? $"{name} {variantNumber}" : name;
        GbxFile = file;
        Variant = variant;
        SerializationWarning = serializationWarning;
    }

    public static IReadOnlyList<ItemVariantSource> FromFile(string name, Gbx<CGameItemModel> file, byte[]? originalBytes = null)
    {
        var warning = GetSerializationWarning(file, originalBytes);
        if (file.Node.EntityModel is NPlugItem_SVariantList { Variants.Length: > 0 } list)
        {
            // Keep unresolved and null entity references: they are still authored variants.
            return list.Variants.Select((variant, index) =>
                new ItemVariantSource(name, file, variant, index + 1, warning)).ToArray();
        }

        // An empty variant list is still an editable/exportable document.
        return new[] { new ItemVariantSource(name, file, null, null, warning) };
    }

    public void Save(Stream destination)
    {
        EnsureSerializerCanPreserveSource();
        GbxFile.Save(destination);
    }

    /// <summary>Combines documents using the first file's item-level metadata.</summary>
    public static void SaveCombined(IReadOnlyList<ItemVariantSource> sources, Stream destination)
    {
        if (sources.Select(source => source.GbxFile).Distinct().Take(2).Count() < 2)
            throw new InvalidOperationException("Choose at least two item files to combine.");

        var documents = sources.Select(source => source.GbxFile).Distinct().ToArray();
        foreach (var source in sources) source.EnsureSerializerCanPreserveSource();
        if (documents.Any(file => file.RefTable?.Files.Count > 0 || file.RefTable?.Resources.Count > 0))
            throw new InvalidOperationException("Combining files with external references is not supported. Export each file separately to preserve its dependencies.");

        var variants = new List<NPlugItem_SVariant>();
        foreach (var source in sources)
        {
            if (source.Model.EntityModel is NPlugItem_SVariantList { Variants.Length: 0 })
                throw new InvalidOperationException($"{source.Name} has an empty variant list. Export it separately.");

            if (source.Variant is { } variant)
            {
                variants.Add(new NPlugItem_SVariant
                {
                    Tags = new Dictionary<string, string>(variant.Tags),
                    HiddenInManualCycle = variant.HiddenInManualCycle,
                    EntityModel = variant.EntityModel,
                    EntityModelFile = variant.EntityModelFile
                });
            }
            else if (source.Model.EntityModel is { } entity)
            {
                variants.Add(new NPlugItem_SVariant { EntityModel = entity });
            }
            else
            {
                throw new InvalidOperationException($"{source.Name} has no entity model to combine. Export it separately.");
            }

        }

        var first = sources[0];
        var originalRoot = first.Model.EntityModel;
        try
        {
            // Version 1 is required to serialize HiddenInManualCycle.
            first.Model.EntityModel = new NPlugItem_SVariantList { Version = 1, Variants = variants.ToArray() };
            first.Save(destination);
        }
        finally
        {
            first.Model.EntityModel = originalRoot;
        }
    }

    private void EnsureSerializerCanPreserveSource()
    {
        if (SerializationWarning is not null)
            throw new InvalidOperationException(SerializationWarning);
    }

    private static string? GetSerializationWarning(Gbx<CGameItemModel> file, byte[]? originalBytes)
    {
        if (originalBytes is null) return null;

        using var output = new MemoryStream();
        file.Save(output);
        return output.ToArray().SequenceEqual(originalBytes)
            ? null
            : "This item cannot be exported safely: the available GBX serializer changes its bytes even before edits. Use Editor++ to save the edited item, or choose a source item that round-trips byte-for-byte.";
    }
}
