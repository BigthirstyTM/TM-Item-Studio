using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// File-only probes: does initializing the same UserInst in an earlier mesh
// make it available when the animated mesh runs its native post-load check?
internal static class FlagGpuWarmup
{
    static CPlugPrefab Prefab(CGameItemModel item) =>
        (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;

    public static int Create(string seed, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var mode in new[] { "SharedInst", "SeparateInst", "EmptyOwner" })
        {
            var item = Gbx.Parse<CGameItemModel>(seed).Node;
            var prefab = Prefab(item);
            var mast = (CPlugStaticObjectModel)prefab.Ents[0].Model!;
            var cloth = ((CPlugDynaObjectModel)prefab.Ents[1].Model!).Mesh!;
            var materials = cloth.CustomMaterials!;
            if (mode == "SeparateInst")
                materials = ((CPlugDynaObjectModel)Prefab(Gbx.Parse<CGameItemModel>(seed).Node).Ents[1].Model!).Mesh!.CustomMaterials!;
            if (mode != "EmptyOwner")
                mast.Mesh!.CustomMaterials = [..mast.Mesh.CustomMaterials!, ..materials];
            else
            {
                var owner = Gbx.Parse<CGameItemModel>(seed).Node;
                var empty = ((CPlugStaticObjectModel)Prefab(owner).Ents[0].Model!);
                empty.Mesh!.Visuals = [];
                empty.Mesh.ShadedGeoms = [];
                empty.Mesh.CustomMaterials = materials;
                empty.Mesh.Materials = [];
                empty.Shape = null;
                empty.IsMeshCollidable = false;
                prefab.Ents = [new CPlugPrefab.EntRef { Model = empty, Rotation = new(0, 0, 0, 1) }, ..prefab.Ents];
            }
            var path = Path.Combine(folder, "TMIS_Warm_" + mode + ".Item.Gbx");
            new Gbx<CGameItemModel>(item).Save(path);
            var read = Gbx.Parse<CGameItemModel>(path).Node;
            var p = Prefab(read);
            var c = ((CPlugDynaObjectModel)p.Ents[^1].Model!).Mesh!;
            if (mode != "SeparateInst" && !ReferenceEquals(((CPlugStaticObjectModel)p.Ents[0].Model!).Mesh!.CustomMaterials![^1].MaterialUserInst,
                c.CustomMaterials![^1].MaterialUserInst)) throw new Exception("Shared UserInst was not retained");
            Console.WriteLine($"PARSED {path}; cloth material indices {string.Join(',', c.ShadedGeoms!.Select(g => g.MaterialIndex))}");
        }
        return 0;
    }
}
