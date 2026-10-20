using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Plug;
using GBX.NET.LZO;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project Tests/TextureLibrary -- <GameData root> <output texture root>");
    return 2;
}

var sourceRoot = Path.GetFullPath(args[0]);
var outputRoot = Path.GetFullPath(args[1]);
if (!Directory.Exists(sourceRoot))
    throw new DirectoryNotFoundException(sourceRoot);

Gbx.LZO = new MiniLZO();

var imagesByRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var imagesByName = new Dictionary<string, List<(string Relative, string FullPath)>>(StringComparer.OrdinalIgnoreCase);
foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
{
    if (!IsTextureFile(file))
        continue;

    var relative = Relative(file);
    imagesByRelativePath[relative] = file;
    var name = Path.GetFileName(file);
    if (!imagesByName.TryGetValue(name, out var imageList))
        imagesByName[name] = imageList = new();
    imageList.Add((relative, file));
}

var materials = new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase);
var missing = new List<MissingReference>();
var failures = new List<ParseFailure>();
var materialFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
    .Where(file => file.EndsWith(".Material.Gbx", StringComparison.OrdinalIgnoreCase))
    .ToArray();

foreach (var materialFile in materialFiles)
{
    var materialRelative = Relative(materialFile);
    var link = Path.ChangeExtension(materialRelative, null)!;
    if (link.EndsWith(".Material", StringComparison.OrdinalIgnoreCase))
        link = link[..^".Material".Length];
    link = link.Replace('/', '\\');
    var environment = link.Split('\\', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    if (!materials.TryGetValue(link, out var entry))
    {
        var name = Path.GetFileName(link);
        entry = new MaterialEntry
        {
            Link = link,
            Environment = environment,
            MaterialFile = materialRelative,
            Aliases =
            [
                NormalizeKey(link),
                NormalizeKey(name),
                NormalizeKey($"tm_{name}_asset")
            ]
        };
        materials[link] = entry;
    }

    try
    {
        var material = Gbx.Parse<CPlugMaterial>(materialFile).Node;
        var colorTarget = (material.CustomMaterial?.GpuFxs1 ?? [])
            .FirstOrDefault(fx => fx.U01.Equals("TargetColor", StringComparison.OrdinalIgnoreCase)
                || fx.U01.Equals("BaseColorTarget", StringComparison.OrdinalIgnoreCase))
            ?.U03?.FirstOrDefault();
        if (colorTarget is { Length: >= 3 })
        {
            entry.Colorable = true;
            entry.TargetColor ??= colorTarget.Take(3).ToArray();
        }
        foreach (var textureSlot in material.CustomMaterial?.Textures ?? [])
        {
            if (textureSlot.Texture is not CPlugBitmap bitmap || bitmap.ImageFile is null
                || string.IsNullOrWhiteSpace(bitmap.ImageFile.FilePath))
                continue;

            var resolved = ResolveTexture(materialRelative, bitmap.ImageFile.FilePath,
                textureSlot.TextureFile?.FilePath, imagesByRelativePath, imagesByName);
            if (resolved is null)
            {
                missing.Add(new MissingReference(materialRelative, textureSlot.Name,
                    bitmap.ImageFile.FilePath, textureSlot.TextureFile?.FilePath));
                continue;
            }

            var role = TextureRole(textureSlot.Name, bitmap.ImageFile.FilePath);
            if (role is not null && !entry.Textures.ContainsKey(role))
                entry.Textures[role] = resolved.Value.Relative;
            else if (!entry.Textures.Values.Contains(resolved.Value.Relative, StringComparer.OrdinalIgnoreCase))
                entry.Auxiliary.Add(resolved.Value.Relative);
        }
    }
    catch (Exception ex)
    {
        failures.Add(new ParseFailure(materialRelative, ex.Message));
    }
}

var entries = materials.Values.Where(entry => entry.Textures.Count > 0).OrderBy(entry => entry.Link, StringComparer.OrdinalIgnoreCase).ToArray();
var files = entries.SelectMany(entry => entry.Textures.Values.Concat(entry.Auxiliary))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

Directory.CreateDirectory(outputRoot);
var copied = 0;
foreach (var relative in files)
{
    if (!imagesByRelativePath.TryGetValue(relative, out var source))
        continue;
    var destination = Path.Combine(outputRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    if (!File.Exists(destination) || new FileInfo(destination).Length != new FileInfo(source).Length)
        File.Copy(source, destination, overwrite: true);
    copied++;
    if (copied % 100 == 0)
        Console.WriteLine($"Copied {copied}/{files.Length} textures.");
}

var manifest = new TextureManifest
{
    Version = 1,
    Kind = "Trackmania2020TextureLibrary",
    Source = "Openplanet Extract/GameData",
    GeneratedAt = DateTimeOffset.UtcNow,
    Stats = new TextureManifestStats
    {
        MaterialEntries = entries.Length,
        TextureFiles = files.Length,
        MissingReferences = missing.Count,
        ParseFailures = failures.Count
    },
    Materials = entries,
    Files = files,
    MissingReferences = missing,
    ParseFailures = failures
};

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
File.WriteAllText(Path.Combine(outputRoot, "manifest.json"), JsonSerializer.Serialize(manifest, jsonOptions) + Environment.NewLine);
File.WriteAllText(Path.Combine(outputRoot, "README.txt"),
    "Trackmania 2020 texture library generated from Openplanet Extract/GameData.\r\n" +
    "Only image files referenced by parsed *.Material.Gbx files are included.\r\n" +
    "The manifest maps full game material links to exact texture files.\r\n");

Console.WriteLine($"Created {outputRoot}");
Console.WriteLine($"Material entries: {entries.Length}; texture files: {files.Length}; missing refs: {missing.Count}; parse failures: {failures.Count}.");
return 0;

string Relative(string path) => Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/');

static bool IsTextureFile(string path)
    => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

static string NormalizeKey(string value) => value.Replace('\\', '/').Trim('/').ToLowerInvariant();

static (string Relative, string FullPath)? ResolveTexture(
    string materialRelative,
    string imageReference,
    string? textureReference,
    IReadOnlyDictionary<string, string> imagesByRelativePath,
    IReadOnlyDictionary<string, List<(string Relative, string FullPath)>> imagesByName)
{
    var materialPath = materialRelative.Replace('\\', '/');
    var environment = materialPath.Split('/')[0];
    var image = imageReference.Replace('\\', '/').TrimStart('/');
    var texture = textureReference?.Replace('\\', '/').Trim('/');
    var candidates = new List<string>();
    if (image.StartsWith("Texture/", StringComparison.OrdinalIgnoreCase))
        candidates.Add($"{environment}/Media/{image}");
    else if (image.Contains("/Media/", StringComparison.OrdinalIgnoreCase))
        candidates.Add(image);
    else
        candidates.Add($"{environment}/Media/Texture/{image}");

    if (!string.IsNullOrWhiteSpace(texture))
    {
        var textureSeparator = texture.LastIndexOf('/');
        if (textureSeparator >= 0)
        {
            var textureDirectory = texture[..textureSeparator];
            candidates.Add($"{textureDirectory}/{image}");
        }
    }
    candidates.Add($"{environment}/Media/Texture/Image/{Path.GetFileName(image)}");

    foreach (var candidate in candidates)
        if (imagesByRelativePath.TryGetValue(candidate, out var fullPath))
            return (candidate, fullPath);

    var name = Path.GetFileName(image);
    if (!imagesByName.TryGetValue(name, out var byName))
        return null;
    return byName.FirstOrDefault(item => item.Relative.StartsWith(environment + "/", StringComparison.OrdinalIgnoreCase)
        && item.Relative.Contains("/Media/Texture/", StringComparison.OrdinalIgnoreCase))
        is { Relative.Length: > 0 } preferred ? preferred : byName.FirstOrDefault();
}

static string? TextureRole(string? slot, string image)
{
    var stem = Path.GetFileNameWithoutExtension(image).ToLowerInvariant();
    if (stem.EndsWith("_huemask") || stem.EndsWith("_huemask2") || stem.EndsWith("_hueshiftmask")) return "hueMask";
    if (stem.EndsWith("_x2")) return "large";
    if (stem.EndsWith("_d") || stem.EndsWith("_t")) return "base";
    if (stem.EndsWith("_n")) return "normal";
    if (stem.EndsWith("_r")) return "surface";
    if (stem.EndsWith("_h")) return "height";
    if (stem.EndsWith("_i")) return "emissive";
    if (stem.EndsWith("_l")) return "light";
    if (stem.EndsWith("_o")) return "overlay";
    if (stem.EndsWith("_m")) return "mask";
    var slotName = slot?.ToLowerInvariant() ?? "";
    if (slotName.Contains("basecolor") || slotName.Contains("diffuse") || slotName.Contains("albedo") || slotName.Contains("color")) return "base";
    if (slotName.Contains("normal") || slotName.Contains("bump")) return "normal";
    if (slotName.Contains("rough") || slotName.Contains("metal") || slotName.Contains("specular")) return "surface";
    if (slotName.Contains("height") || slotName.Contains("depth")) return "height";
    if (slotName.Contains("illum") || slotName.Contains("energy") || slotName.Contains("light") || slotName.Contains("emissive")) return "emissive";
    if (slotName.Contains("overlay")) return "overlay";
    if (slotName.Contains("mask") || slotName.Contains("opacity")) return "mask";
    return null;
}

sealed class TextureManifest
{
    public int Version { get; init; }
    public string Kind { get; init; } = "";
    public string Source { get; init; } = "";
    public DateTimeOffset GeneratedAt { get; init; }
    public TextureManifestStats Stats { get; init; } = new();
    public IReadOnlyList<MaterialEntry> Materials { get; init; } = [];
    public IReadOnlyList<string> Files { get; init; } = [];
    public IReadOnlyList<MissingReference> MissingReferences { get; init; } = [];
    public IReadOnlyList<ParseFailure> ParseFailures { get; init; } = [];
}

sealed class TextureManifestStats
{
    public int MaterialEntries { get; init; }
    public int TextureFiles { get; init; }
    public int MissingReferences { get; init; }
    public int ParseFailures { get; init; }
}

sealed class MaterialEntry
{
    public string Link { get; init; } = "";
    public string Environment { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public Dictionary<string, string> Textures { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Auxiliary { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string MaterialFile { get; init; } = "";
    public bool Colorable { get; set; }
    public float[]? TargetColor { get; set; }
}

sealed record MissingReference(string Material, string? Slot, string Image, string? Texture);
sealed record ParseFailure(string Material, string Error);
