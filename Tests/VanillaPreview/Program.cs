using GBX.NET;
using TM_Item_Studio.Models;

// Optional local corpus test. No game assets are redistributed by this project.
Gbx.LZO = new GBX.NET.LZO.MiniLZO();
if (args.Length == 0) throw new ArgumentException("Usage: VanillaPreview <extracted GameData> [relative GBX paths...]");
var root = Path.GetFullPath(args[0]);
var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
var indexed = files.ToDictionary(path => Normalize(Path.GetRelativePath(root, path)), StringComparer.OrdinalIgnoreCase);
var requested = args.Length > 1 ? args.Skip(1).ToArray() : new[] {
    "Stadium/Items/CactusMedium.Item.Gbx", "Stadium/Items/CactusVerySmall.Item.Gbx",
    "Stadium/Media/Static/Vegetation/Cactus.StaticObject.Gbx", "Stadium/Media/Static/Vegetation/Cactus.Mesh.Gbx",
    "Vehicles/Items/Cars/CarSport.Item.Gbx", "Vehicles/Items/Cars/CarSnow.Item.Gbx",
    "Vehicles/Items/Cars/CarRally.Item.Gbx", "Vehicles/Items/Cars/CarDesert.Item.Gbx",
    "ShootMania/Items/Characters/CharacterPilot.Item.gbx",
    "Skins/Models/CharacterPilot/Stadium/Female/Player.Mesh.gbx"
};
var failed = 0;
foreach (var relative in requested)
{
    try
    {
        var path = Path.IsPathRooted(relative) ? relative : indexed[Normalize(relative)];
        var original = File.ReadAllBytes(path);
        Gbx document;
        try { document = Gbx.Parse(new MemoryStream(original), new GbxReadSettings { SafeSkippableChunks = true, IgnoreExceptionsInBody = true }); }
        catch { document = Gbx.ParseHeader(new MemoryStream(original)); }
        var skins = document.RefTable?.Files.Where(file => file.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && file.FilePath.Contains("Skins", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        string? skinPath = null;
        foreach (var skin in skins)
        {
            var basePath = Normalize(skin.FilePath)[..^4];
            skinPath = new[] { "/mainbody.mesh.gbx", "/standard/mainbody.mesh.gbx", "/male/player.mesh.gbx" }
                .Select(suffix => indexed.GetValueOrDefault(basePath + suffix)).FirstOrDefault(path => path is not null);
            if (skinPath is not null) break;
        }
        if (skinPath is not null) document = Gbx.Parse(skinPath, new GbxReadSettings { SafeSkippableChunks = true, IgnoreExceptionsInBody = true });
        var dependencies = await GbxDependencyLoader.HydrateAsync(document, reference => {
            var key = Normalize(reference);
            var matches = indexed.Where(pair => pair.Key == key || pair.Key.EndsWith('/' + key)).ToArray();
            return Task.FromResult(matches.Length != 1 ? null : new GbxDependencyLoader.ResolvedFile(matches[0].Key, File.ReadAllBytes(matches[0].Value)));
        });
        var scene = ItemScene.Build(document.Node, 0, 0, resolveExternal: true).Preview;
        if (scene.Geometry.Count == 0) throw new Exception("No decoded geometry.");
        if (scene.Materials.Count == 0 || scene.Materials.Any(material => string.IsNullOrWhiteSpace(material.GameMaterialName)))
            throw new Exception("Material identities missing.");
        if (scene.Geometry.Any(geometry => geometry.Positions.Any(value => !float.IsFinite(value)))) throw new Exception("Nonfinite vertices.");
        if (!File.ReadAllBytes(path).SequenceEqual(original)) throw new Exception("Source archive changed.");
        Console.WriteLine($"PASS {relative}: {scene.Geometry.Count} geometries, {scene.Materials.Count} materials; skin={skinPath is not null}; partial={document.Body.Exception is not null}; dependencies={dependencies.LoadedCount}");
    }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {relative}: {ex.Message}"); }
}
Console.WriteLine($"{requested.Length - failed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
