using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 2 && args[0] == "--generate") { Fixture.Generate(args[1]); return; }
var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
foreach (var path in args)
{
    var document = Gbx.Parse<CGameItemModel>(path);
    var prefab = document.Node.EntityModel is NPlugItem_SVariantList variants
        ? (CPlugPrefab)variants.Variants![0].EntityModel! : (CPlugPrefab)document.Node.EntityModel!;
    Console.WriteLine(JsonSerializer.Serialize(new { path, document.Node.Ident,
        references = document.RefTable?.Files.Select(f => new { f.FilePath, f.Flags, f.UseFile }),
        entities = prefab.Ents!.Select(e => new { model = e.Model!.GetType().Name, e.Position, e.Rotation,
            parameters = e.Params,
            dynaParameters = e.Params is NPlugDynaObjectModel_SInstanceParams ip ? new { ip.Version, ip.PeriodSc, ip.PeriodScMax, ip.Phase01, ip.Phase01Max, ip.IsKinematic, ip.CastStaticShadow, ip.TextureId } : null,
            body = e.Model is CPlugDynaObjectModel d ? new { d.Version, d.IsStatic, d.DynamizeOnSpawn,
                staticShape = d.StaticShape?.GetType().Name, dynaShape = d.DynaShape?.GetType().Name,
                staticShapeFile = d.StaticShapeFile?.FilePath, dynaShapeFile = d.DynaShapeFile?.FilePath,
                d.Mass, d.BreakSpeedKmh, d.U01, d.U02, d.U03, d.U04, d.U05, d.U06, d.U07, d.U08, d.U09,
                meshHash = MeshHash(d.Mesh!), locAnim = d.LocAnim?.GetType().Name } : null,
            staticMeshHash = e.Model is CPlugStaticObjectModel s ? MeshHash(s.Mesh!) : null }) }, json));
}

static string MeshHash(CPlugSolid2Model mesh) { using var memory = new MemoryStream(); new Gbx<CPlugSolid2Model>(mesh).Save(memory); return Convert.ToHexString(SHA256.HashData(memory.ToArray())); }
