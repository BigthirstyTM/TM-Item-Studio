using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Engines.GameData;

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length < 1) throw new ArgumentException("Usage: FxMeshVariants <output-directory>");
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var root = FindRoot();
var fixtures = new[]
{
    ("TMIS_Custom_Fogger_PyramidMesh", Path.Combine(root, "artifacts", "flag-shapes-20261021", "models", "TMIS_Shape11_Control.Item.Gbx"), "rounded control mesh host"),
    ("TMIS_Custom_Sparkler_DiamondMesh", Path.Combine(root, "artifacts", "flag-shapes-20261021", "models", "TMIS_Shape11_Swallowtail.Item.Gbx"), "angular swallowtail/diamond mesh host")
};
var rows = new List<object>();
foreach (var (id, source, description) in fixtures)
{
    var document = Gbx.Parse<CGameItemModel>(source, new GbxReadSettings { SafeSkippableChunks = true });
    var item = document.Node ?? throw new InvalidOperationException($"Missing item node in {source}");
    item.Ident = new Ident(id, item.Ident?.Collection ?? new Id("Stadium2020"), "TM_Item_Studio");
    item.Name = id;
    item.Description = description;
    item.PageName = "Items";
    item.CatalogPosition = 1;
    var path = Path.Combine(output, id + ".Item.Gbx");
    document.Save(path);
    var reopened = Gbx.Parse<CGameItemModel>(path, new GbxReadSettings { SafeSkippableChunks = true });
    var bytes = File.ReadAllBytes(path);
    rows.Add(new
    {
        id,
        source,
        output = path,
        description,
        bytes = bytes.Length,
        sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
        entity = reopened.Node?.EntityModel?.GetType().Name,
        refs = reopened.RefTable?.Files.Count ?? 0,
        status = "typed mesh host; separate from opaque FX carrier until native composite assembly is supported"
    });
}
var reportPath = Path.Combine(output, "fx-mesh-variants.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));

static string FindRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "TM-Item-Studio.csproj"))) current = current.Parent;
    return current?.FullName ?? throw new DirectoryNotFoundException("TM-Item-Studio root was not found.");
}
