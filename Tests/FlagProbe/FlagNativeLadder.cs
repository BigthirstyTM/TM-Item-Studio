using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using System.Collections;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// Installation-only controls. Start with parsed native archives and preserve
// their reference tables. These are not portable user-item deliverables.
internal static class FlagNativeLadder
{
    const string Data = @"C:\Users\PC\OpenplanetNext\Extract\GameData";
    public static int Create(string output, bool chain = false, bool fixedReferences = false)
    {
        if (Directory.Exists(output)) throw new IOException("Preserve previous fixture");
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        void Save(Gbx doc, string relative, string change)
        {
            var path = Path.Combine(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            doc.Save(path);
            var parsed = Gbx.Parse(path);
            rows.Add(new { relative, change, bytes=new FileInfo(path).Length,
                ancestor=parsed.RefTable?.AncestorLevel,
                references=parsed.RefTable?.Files.Select(f=>new {f.FilePath,f.Flags,f.UseFile}).ToArray(),
                sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) });
        }
        foreach (var mode in chain ? new[] { "UniqueIdent", "EmptyIdent", "ExternalLong", "InlineDyna", "InlineMesh" } : new[] { "Roundtrip", "Name" })
        {
            var doc = Gbx.Parse<CGameItemModel>(Path.Combine(Data,@"Stadium\Items\Flag16m.Item.Gbx"));
            if(mode=="Name") doc.Node.Name="TMIS native name";
            if(mode=="UniqueIdent") doc.Node.Ident=doc.Node.Ident! with {Id="TMIS_Ladder_UniqueIdent"};
            if(mode=="EmptyIdent") doc.Node.Ident=doc.Node.Ident! with {Id=""};
            if(mode is "ExternalLong" or "InlineDyna" or "InlineMesh")
            {
                var variant=((NPlugItem_SVariantList)doc.Node.EntityModel!).Variants![0];
                if(mode=="ExternalLong") variant.References.EntityModel.File!.FilePath=@"Media\Prefab\Items\Flag\TMIS_Ladder_Long250.Prefab.Gbx";
                else
                {
                    var prefab=Gbx.Parse<CPlugPrefab>(Path.Combine(Data,@"Stadium\Media\Prefab\Items\Flag\Flag16m.Prefab.Gbx"));
                    // Item references are relative to Stadium. Prefab material
                    // and dyna references were relative to Stadium/Media.
                    foreach(var f in prefab.RefTable!.Files) f.FilePath=@"Media\"+f.FilePath;
                    variant.EntityModel=prefab.Node;if (variant.References.EntityModel.Descriptor is not null) variant.References.EntityModel.Clear();
                    var entry=prefab.Node.Ents![1];
                    var dyna=Gbx.Parse<CPlugDynaObjectModel>(Path.Combine(Data,@"Stadium\Media\Dyna\Flag\Flag.DynaObject.Gbx"));
                    entry.Model=dyna.Node;if (entry.References.Model.Descriptor is not null) entry.References.Model.Clear();
                    if(mode=="InlineDyna") dyna.Node.References.Mesh.File!.FilePath=@"Media\Dyna\Flag\TMIS_Ladder_Long250.Mesh.Gbx";
                    else
                    {
                        var mesh=Gbx.Parse<CPlugSolid2Model>(Path.Combine(Data,@"Stadium\Media\Dyna\Flag\Flag.Mesh.Gbx"));
                        foreach(var f in mesh.RefTable!.Files) f.FilePath=@"Media\"+f.FilePath;
                        dyna.Node.Mesh=mesh.Node;if (dyna.Node.References.Mesh.Descriptor is not null) dyna.Node.References.Mesh.Clear();
                    }
                }
            }
            if(fixedReferences && mode is "InlineDyna" or "InlineMesh")
            {
                // RefTable.Files are descriptors; the parsed node graph may
                // hold separate file-reference instances. Remap those actual
                // references, and assert saved paths below.
                var graph=((NPlugItem_SVariantList)doc.Node.EntityModel!).Variants![0].EntityModel!;
                Remap(graph);
            }
            Save(doc,$"GameData/Stadium/Items/TMIS_Ladder_{(fixedReferences?"Fixed":"")}{mode}.Item.Gbx",mode);
        }
        if(chain && !fixedReferences)
        {
            var prefab=Gbx.Parse<CPlugPrefab>(Path.Combine(Data,@"Stadium\Media\Prefab\Items\Flag\Flag16m.Prefab.Gbx"));
            prefab.Node.Ents![1].References.Model.File!.FilePath=@"Dyna\Flag\TMIS_Ladder_Long250.DynaObject.Gbx";
            Save(prefab,"GameData/Stadium/Media/Prefab/Items/Flag/TMIS_Ladder_Long250.Prefab.Gbx","External dyna alias only");
            var dyna=Gbx.Parse<CPlugDynaObjectModel>(Path.Combine(Data,@"Stadium\Media\Dyna\Flag\Flag.DynaObject.Gbx"));
            dyna.Node.References.Mesh.File!.FilePath="TMIS_Ladder_Long250.Mesh.Gbx";
            Save(dyna,"GameData/Stadium/Media/Dyna/Flag/TMIS_Ladder_Long250.DynaObject.Gbx","External mesh alias only");
        }
        foreach(var mode in chain ? Array.Empty<string>() : new[] { "Roundtrip", "Width1025", "Long250" })
        {
            var doc=Gbx.Parse<CPlugSolid2Model>(Path.Combine(Data,@"Stadium\Media\Dyna\Flag\Flag.Mesh.Gbx"));
            if(mode!="Roundtrip")
                foreach(var visual in doc.Node.Visuals!.OfType<CPlugVisualIndexedTriangles>())
                {
                    var scale=mode=="Long250"?2.5f:1.025f;
                    visual.Vertices=visual.Vertices.Select(p=>p with {Position=new(p.Position.X*scale,p.Position.Y,p.Position.Z)}).ToArray();
                    foreach(var stream in visual.VertexStreams)
                        if(stream.Positions is {} positions)
                            stream.Positions=positions.Select(p=>new Vec3(p.X*scale,p.Y,p.Z)).ToArray();
                }
            Save(doc,$"GameData/Stadium/Media/Dyna/Flag/TMIS_Ladder_{mode}.Mesh.Gbx",mode);
        }
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions {WriteIndented=true}));
        return 0;
    }

    static void Remap(object root)
    {
        var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object? value)
        {
            if(value is null || value is string || !seen.Add(value))return;
            if(value is GbxRefTableFile file)
            {
                if(file.FilePath.StartsWith("Material\\",StringComparison.Ordinal))file.FilePath="Media\\"+file.FilePath;
                return;
            }
            if(value is GbxRefTable)return;
            if(value is IEnumerable seq) {foreach(var child in seq)Walk(child);return;}
            var type=value.GetType();
            if(type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("GBX.NET")!=true)return;
            foreach(var p in type.GetProperties(BindingFlags.Instance|BindingFlags.Public))
                if(p.CanRead && p.GetIndexParameters().Length==0 && p.Name is not "Gbx" and not "Chunks")Walk(p.GetValue(value));
            foreach(var f in type.GetFields(BindingFlags.Instance|BindingFlags.Public))Walk(f.GetValue(value));
        }
        Walk(root);
    }
}
