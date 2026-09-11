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
    public Gbx<CGameItemModel> GbxFile { get; }
    public CGameItemModel Model => GbxFile.Node;
    public NPlugItem_SVariant? Variant { get; }
    public CMwNod? PreviewRoot => Variant is null ? Model : Variant.EntityModel;

    private ItemVariantSource(string name, Gbx<CGameItemModel> file, NPlugItem_SVariant? variant)
    {
        Name = name;
        GbxFile = file;
        Variant = variant;
    }

    public static IReadOnlyList<ItemVariantSource> FromFile(string name, Gbx<CGameItemModel> file)
    {
        if (file.Node.EntityModel is NPlugItem_SVariantList { Variants.Length: > 0 } list)
        {
            // Keep unresolved and null entity references: they are still authored variants.
            return list.Variants.Select((variant, index) =>
                new ItemVariantSource($"{name} {index + 1}", file, variant)).ToArray();
        }

        // An empty variant list is still an editable/exportable document.
        return new[] { new ItemVariantSource(name, file, null) };
    }

    public void Save(Stream destination) => GbxFile.Save(destination);

    /// <summary>Combines documents using the first file's item-level metadata.</summary>
    public static void SaveCombined(IReadOnlyList<ItemVariantSource> sources, Stream destination)
    {
        if (sources.Select(source => source.GbxFile).Distinct().Take(2).Count() < 2)
            throw new InvalidOperationException("Choose at least two item files to combine.");

        var documents = sources.Select(source => source.GbxFile).Distinct().ToArray();
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
}
