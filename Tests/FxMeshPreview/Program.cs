using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;
using TM_Item_Studio.Models;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var files = args.Length > 0 ? args : new[]
{
    @"C:\Users\PC\Documents\Trackmania\Items\ChatGPT\PackedOnlyV2\TestItem_Sparkler_BitmapRefs_v3.Item.Gbx",
    @"C:\Users\PC\Documents\Trackmania\Items\ShadocAlex Items\FogVolumetric2\FogVolumetric1_FogEmissionRate_0.Item.Gbx",
    Path.Combine(root, "artifacts/fx-clones/fx-standalone-20261025/TMIS_CustomSparklerFlare_Builtin.Item.Gbx"),
    Path.Combine(root, "artifacts/fx-clones/fx-sequential-bitmap-20261025/TMIS_Sparkler_SequentialPixelsLookbackFixed.Item.Gbx")
};
var checks = new List<string>();
var reports = new List<object>();
var auditItemY = Environment.GetEnvironmentVariable("TMIS_EXPECT_ITEM_Y");
foreach (var path in files)
{
    if (!File.Exists(path)) continue;
    var bytes = File.ReadAllBytes(path); var snapshot = bytes.ToArray();
    var preview = FxMeshPreview.Read(bytes);
    Check(preview.Proof.PreviewOnly && preview.Proof.OriginalBodyRestorableExactly
        && preview.Proof.MeshPrefixPreservedExactly && preview.Proof.RemovedFxBytes > 0,
        $"{Path.GetFileName(path)}: exact reversible preview-only splice");
    Check(bytes.AsSpan().SequenceEqual(snapshot), $"{Path.GetFileName(path)}: source bytes unchanged");
    var scene = ItemScene.Build(preview.PreviewRoot, 0).Preview;
    var mesh = scene.Geometry.Where(geometry => !geometry.IsCollision).ToArray();
    Check(mesh.Length > 0 && mesh.Sum(geometry => geometry.Positions.Length) > 0
        && mesh.Sum(geometry => geometry.Indices.Length) > 0,
        $"{Path.GetFileName(path)}: original typed mesh reaches scene geometry");
    object? studioExportAudit = null;
    if (auditItemY is not null)
    {
        var expectedY = float.Parse(auditItemY, System.Globalization.CultureInfo.InvariantCulture);
        var transform = FxEmitterTransform.Read(bytes);
        var identity = FxIdentity.Read(bytes);
        var header = Gbx.ParseHeader<CGameItemModel>(new MemoryStream(bytes));
        Check(Math.Abs(transform.Primary.X) < .0001f && Math.Abs(transform.Primary.Z) < .0001f
            && Math.Abs(transform.Primary.Y - expectedY) < .0001f,
            $"{Path.GetFileName(path)}: exported native expressions resolve to requested physical item-space point");
        Check(header.Node.Ident?.Id is { Length: > 0 } id && (identity.Id is null || identity.Id == id)
            && header.Node.Ident.Author == identity.Author,
            $"{Path.GetFileName(path)}: native header/body identity and author agree");
        Check(header.Node.Name == identity.Name && identity.Description == identity.Name
            && !string.IsNullOrWhiteSpace(identity.Name),
            $"{Path.GetFileName(path)}: native header/body Name agrees and body Description carries the same export name");
        Check((header.RefTable?.Files.Count ?? 0) == 0 && (header.RefTable?.Resources.Count ?? 0) == 0,
            $"{Path.GetFileName(path)}: exported item has zero external file/resource references");
        studioExportAudit = new { headerIdentity = new { id = header.Node.Ident?.Id,
                author = header.Node.Ident?.Author, name = header.Node.Name },
            itemPoint = transform.Primary, owner = transform.Owner,
            identity, fileBytes = bytes.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            externalFiles = header.RefTable?.Files.Count ?? 0, externalResources = header.RefTable?.Resources.Count ?? 0 };
    }
    reports.Add(new { file = Path.GetFileName(path), preview.Proof,
        studioExportAudit,
        nativeEmitterPositions = FxEmissionPoint.Read(bytes).Emitters.Select(emitter => emitter.Position),
        meshParts = mesh.Length, vertices = mesh.Sum(geometry => geometry.Positions.Length / 3),
        triangles = mesh.Sum(geometry => geometry.Indices.Length / 3),
        bounds = mesh.Select(geometry => new { geometry.BoundsMin, geometry.BoundsMax }),
        diagnostics = scene.Diagnostics });
}
Check(reports.Count > 0, "At least one actual FX fixture tested");
Check(!FxMeshPreview.TryRead(new byte[30], out var invalid, out var reason)
    && invalid is null && !string.IsNullOrWhiteSpace(reason), "Invalid item rejected without a preview");
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, checks, reports }, new JsonSerializerOptions { WriteIndented = true }));
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidDataException("FAILED: " + message);
    checks.Add(message);
}
