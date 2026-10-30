using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

// File-only installation package. Deliberately separate from a portable,
// embedded user item: it depends on native game-drive material resolution.
internal static class FlagNativePackage
{
    const string Data = @"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium";
    public static int Create(string output)
    {
        if(Directory.Exists(output))throw new IOException("Preserve previous fixture");
        Directory.CreateDirectory(output);
        var rows=new List<object>();
        void Save(Gbx doc,string relative,string mode)
        {
            var path=Path.Combine(output,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            doc.Save(path);Gbx.Parse(path);
            rows.Add(new {relative,mode,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))});
        }
        foreach(var mode in new[]{"Long","Swallowtail","Round"})
        {
            var name="TMIS_NativePackage_"+mode;
            var item=Gbx.Parse<CGameItemModel>(Path.Combine(Data,@"Items\Flag16m.Item.Gbx"));
            item.Node.Ident=item.Node.Ident! with {Id=name};item.Node.Name=name;
            ((NPlugItem_SVariantList)item.Node.EntityModel!).Variants![0].References.EntityModel.File!.FilePath=@"Media\Prefab\Items\Flag\"+name+".Prefab.Gbx";
            Save(item,"GameData/Stadium/Items/"+name+".Item.Gbx",mode);
            var prefab=Gbx.Parse<CPlugPrefab>(Path.Combine(Data,@"Media\Prefab\Items\Flag\Flag16m.Prefab.Gbx"));
            prefab.Node.Ents![1].References.Model.File!.FilePath=@"Dyna\Flag\"+name+".DynaObject.Gbx";
            Save(prefab,"GameData/Stadium/Media/Prefab/Items/Flag/"+name+".Prefab.Gbx",mode);
            var dyna=Gbx.Parse<CPlugDynaObjectModel>(Path.Combine(Data,@"Media\Dyna\Flag\Flag.DynaObject.Gbx"));
            dyna.Node.References.Mesh.File!.FilePath=name+".Mesh.Gbx";
            Save(dyna,"GameData/Stadium/Media/Dyna/Flag/"+name+".DynaObject.Gbx",mode);
            var mesh=Gbx.Parse<CPlugSolid2Model>(Path.Combine(Data,@"Media\Dyna\Flag\Flag.Mesh.Gbx"));
            foreach(var v in mesh.Node.Visuals!.OfType<CPlugVisualIndexedTriangles>())
            {
                var uvs=v.Vertices.Length>0?v.TexCoords[0].TexCoords.Select(t=>t.UV).ToArray():v.VertexStreams.Single(s=>s.Positions is {Length:>0}).UVs[0];
                var minU=uvs.Min(t=>t.X);var maxU=uvs.Max(t=>t.X);var minV=uvs.Min(t=>t.Y);var maxV=uvs.Max(t=>t.Y);
                Vec3 Deform(Vec3 p,Vec2 uv)
                {
                    var u=(uv.X-minU)/(maxU-minU);var t=(uv.Y-minV)/(maxV-minV);
                    if(mode=="Long")return new(p.X*2.5f,p.Y,p.Z);
                    if(mode=="Swallowtail")return new(p.X*(.6f+1.15f*MathF.Abs(2*t-1)),p.Y,p.Z);
                    var a=2*u-1;var b=2*t-1;
                    var dx=a*MathF.Sqrt(1-b*b/2);var dy=b*MathF.Sqrt(1-a*a/2);
                    return new(p.X+2.5f*(1+dx)-4*u,p.Y+1.25f+2.5f*dy-2.5f*t,p.Z);
                }
                v.Vertices=v.Vertices.Select((p,i)=>p with {Position=Deform(p.Position,uvs[i])}).ToArray();
                foreach(var s in v.VertexStreams)
                    if(s.Positions is {Length:>0})s.Positions=s.Positions.Select((p,i)=>Deform(p,s.UVs[0][i])).ToArray();
                var points=v.Vertices.Select(p=>p.Position).Concat(v.VertexStreams.SelectMany(s=>s.Positions??[])).ToArray();
                var min=new Vec3(points.Min(p=>p.X),points.Min(p=>p.Y),points.Min(p=>p.Z));
                var max=new Vec3(points.Max(p=>p.X),points.Max(p=>p.Y),points.Max(p=>p.Z));
                v.BoundingBox=new((min.X+max.X)/2,(min.Y+max.Y)/2,(min.Z+max.Z)/2,(max.X-min.X)/2,(max.Y-min.Y)/2,(max.Z-min.Z)/2);
            }
            Save(mesh,"GameData/Stadium/Media/Dyna/Flag/"+name+".Mesh.Gbx",mode);
        }
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
