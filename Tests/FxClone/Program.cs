using System.Security.Cryptography;
using System.Text.Json;
using System.Collections;
using System.Reflection;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using GBX.NET.Managers;
using TM_Item_Studio.Models;

// Identity-only vanilla carrier clone. This intentionally leaves the prefab,
// external FxSys and particle dependencies untouched; it is the baseline fixture
// used before any one-setting native probe.
Gbx.LZO = new GBX.NET.LZO.MiniLZO();

if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Usage: FxClone --source <ShowFogger8M.Item.Gbx|Sparkler8m.Item.Gbx> --output <clone.Item.Gbx> --id <new-id> [--name <display-name>] [--author <author>] [--preserve-source-metadata] [--inline-prefab <Prefab.Gbx>] [--direct-prefab] [--empty-id] [--strip-fx] [--rooted-game-graph] [--game-fid-refs <game|stadium|media>] [--opaque-preserve]");
    Console.WriteLine("       FxClone --verify <clone.Item.Gbx>");
    Console.WriteLine("       FxClone --inspect <item.Item.Gbx>");
    return;
}

if (args[0].Equals("--inspect", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length != 2) throw new ArgumentException("Usage: FxClone --inspect <item.Item.Gbx>");
    Inspect(args[1]);
    return;
}

if (args[0].Equals("--inspect-prefab", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length != 2) throw new ArgumentException("Usage: FxClone --inspect-prefab <prefab.Item.Gbx>");
    InspectPrefab(args[1]);
    return;
}

if (args[0].Equals("--inspect-header", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length != 2) throw new ArgumentException("Usage: FxClone --inspect-header <asset.Gbx>");
    InspectHeader(args[1]);
    return;
}

if (args[0].Equals("--verify", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length != 2) throw new ArgumentException("Usage: FxClone --verify <clone.Item.Gbx>");
    Verify(args[1]);
    return;
}

var sourcePath = Required("--source");
var outputPath = Required("--output");
var rawId = Required("--id");
var id = args.Any(value => value.Equals("--id-exact", StringComparison.OrdinalIgnoreCase))
    ? rawId.Trim().Replace('/', '\\')
    : NormalizeId(rawId);
var preserveSourceMetadata = args.Any(value => value.Equals("--preserve-source-metadata", StringComparison.OrdinalIgnoreCase));
var inlinePrefabPath = Optional("--inline-prefab");
var directPrefab = args.Any(value => value.Equals("--direct-prefab", StringComparison.OrdinalIgnoreCase));
var emptyId = args.Any(value => value.Equals("--empty-id", StringComparison.OrdinalIgnoreCase));
var stripFx = args.Any(value => value.Equals("--strip-fx", StringComparison.OrdinalIgnoreCase));
var rootedGameGraph = args.Any(value => value.Equals("--rooted-game-graph", StringComparison.OrdinalIgnoreCase));
var gameFidRefs = Optional("--game-fid-refs")?.ToLowerInvariant();
var opaquePreserve = args.Any(value => value.Equals("--opaque-preserve", StringComparison.OrdinalIgnoreCase));
var name = Optional("--name") ?? id;
var author = Optional("--author") ?? "TM_Item_Studio";

if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source item was not found.", sourcePath);
if (File.Exists(outputPath)) throw new IOException($"Refusing to overwrite existing clone: {outputPath}");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

var sourceBytes = File.ReadAllBytes(sourcePath);
if (opaquePreserve)
{
    CreateOpaqueClone(sourceBytes, sourcePath, outputPath, id, name);
    return;
}
var source = Parse(sourceBytes, sourcePath);
var item = source.Node ?? throw new InvalidOperationException("Source has no CGameItemModel node.");
if (item.EntityModel is not NPlugItem_SVariantList variants || variants.Variants is not { Length: > 0 })
    throw new InvalidOperationException("The source is not a non-empty NPlugItem_SVariantList carrier.");
if (!variants.Variants.Any(variant => variant.References.EntityModel.File is not null || variant.EntityModel is CPlugPrefab))
    throw new InvalidOperationException("The source has no prefab carrier to preserve.");
if (source.Body?.Exception is not null)
    throw new InvalidOperationException($"The source has an incomplete body parse: {source.Body.Exception.Message}");

if (!string.IsNullOrWhiteSpace(inlinePrefabPath))
{
    var prefabDocument = Gbx.Parse<CPlugPrefab>(inlinePrefabPath,
        new GbxReadSettings { SafeSkippableChunks = true });
    var prefab = prefabDocument.Node ?? throw new InvalidOperationException("Prefab has no node.");
    if (prefabDocument.RefTable is not null)
    {
        foreach (var file in prefabDocument.RefTable.Files)
            if (!file.FilePath.StartsWith("Media\\", StringComparison.OrdinalIgnoreCase))
                file.FilePath = "Media\\" + file.FilePath;
    }
    PrefixPrefabReferencePaths(prefab);
    if (stripFx)
        prefab.Ents = prefab.Ents?.Where(entry => entry.Model is not null).ToArray();
    if (directPrefab)
    {
        item.EntityModel = prefab;
    }
    else
    {
        var variant = ((NPlugItem_SVariantList)item.EntityModel).Variants![0];
        variant.EntityModel = prefab;
        if (variant.References.EntityModel.Descriptor is not null) variant.References.EntityModel.Clear();
    }
}

// Experimental user-package layout: root item references are rooted three
// levels above Items (the same shape used by game-drive packages), while the
// copied graph is staged under Documents\\Trackmania\\Stadium. This is kept
// opt-in because it changes reference resolution semantics deliberately.
if (rootedGameGraph && source.RefTable is not null)
{
    source.RefTable.AncestorLevel = 3;
    foreach (var file in source.RefTable.Files)
    {
        if (file.FilePath.StartsWith("Media\\", StringComparison.OrdinalIgnoreCase))
            file.FilePath = "Stadium\\" + file.FilePath;
    }
}

if (gameFidRefs is not null && source.RefTable is not null)
{
    var prefix = gameFidRefs switch
    {
        "game" => "GameData\\Stadium\\",
        "stadium" => "Stadium\\",
        "media" => "",
        _ => throw new ArgumentException("--game-fid-refs must be game, stadium, or media.")
    };
    source.RefTable.AncestorLevel = 0;
    foreach (var file in source.RefTable.Files)
    {
        if (file.FilePath.StartsWith("Media\\", StringComparison.OrdinalIgnoreCase))
            file.FilePath = prefix + file.FilePath;
        file.UseFile = true;
    }

    // Keep the typed NodeRefs in sync with the table. The item writer rebuilds
    // root references from these fields, so changing only RefTable.Files is
    // intentionally insufficient.
    if (item.EntityModel is NPlugItem_SVariantList rootedVariants && rootedVariants.Variants is { Length: > 0 })
    {
        var prefabRef = source.RefTable.Files.First(file => file.FilePath.EndsWith("Prefab.Gbx", StringComparison.OrdinalIgnoreCase));
        rootedVariants.Variants[0].References.EntityModel.Descriptor = new GbxRefTableFile(source.RefTable, prefabRef.NodeIndex, true, prefabRef.FilePath);
        var placementRef = source.RefTable.Files.First(file => file.FilePath.EndsWith("PlaceParam.Gbx", StringComparison.OrdinalIgnoreCase));
        item.DefaultPlacement = null;
        item.References.DefaultPlacement.Descriptor = new GbxRefTableFile(source.RefTable, placementRef.NodeIndex, true, placementRef.FilePath);
    }
}

var oldId = item.Ident?.Id;
var oldName = item.Name;
var oldRefs = source.RefTable?.Files.Select(file => file.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? [];

// Change identity only. Do not touch EntityModel, variants, RefTable, placement,
// prefab owner transforms, FxSys references, or any opaque body chunks.
item.Ident = new Ident(emptyId ? "" : id, item.Ident?.Collection ?? new Id("Stadium2020"), preserveSourceMetadata ? item.Ident?.Author : author);
if (!preserveSourceMetadata)
{
    item.Name = name;
    item.Description = name;
    if (string.IsNullOrWhiteSpace(item.PageName)) item.PageName = "Items";
    if (item.CatalogPosition <= 0) item.CatalogPosition = 1;
}

using (var output = File.Create(outputPath)) source.Save(output);
Verify(outputPath);

var manifestPath = Path.ChangeExtension(outputPath, ".fxclone.json");
var clone = Parse(File.ReadAllBytes(outputPath), outputPath);
var cloneItem = clone.Node!;
var cloneRefs = clone.RefTable?.Files.Select(file => file.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? [];
var manifest = new
{
    source = Path.GetFullPath(sourcePath),
    output = Path.GetFullPath(outputPath),
    sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)),
    outputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputPath))),
    originalId = oldId,
    originalName = oldName,
    cloneId = cloneItem.Ident?.Id,
    cloneName = cloneItem.Name,
    author = cloneItem.Ident?.Author,
    itemType = cloneItem.ItemType.ToString(),
    entityModel = cloneItem.EntityModel?.GetType().FullName,
    variantCount = cloneItem.EntityModel is NPlugItem_SVariantList cloneList
        ? cloneList.Variants?.Length ?? 0
        : cloneItem.EntityModel is CPlugPrefab ? 1 : 0,
    rootReferencesBefore = oldRefs,
    rootReferencesAfter = cloneRefs,
    referenceAncestorLevel = clone.RefTable?.AncestorLevel,
    referenceEntries = clone.RefTable?.Files.Select(file => new { file.FilePath, file.Flags, file.UseFile, file.NodeIndex }).ToArray(),
    fxCarrierPreserved = cloneRefs.Any(path => path.EndsWith(".Prefab.Gbx", StringComparison.OrdinalIgnoreCase))
        || cloneItem.EntityModel is NPlugItem_SVariantList { Variants: { Length: > 0 } cloneVariants }
            && cloneVariants[0].EntityModel is CPlugPrefab,
    effectPayloadChanged = false,
    nativeRuntimeVerdict = "not-tested"
};
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

string Required(string flag)
{
    var index = Array.FindIndex(args, value => value.Equals(flag, StringComparison.OrdinalIgnoreCase));
    if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        throw new ArgumentException($"Missing {flag}.");
    return args[index + 1];
}

string? Optional(string flag)
{
    var index = Array.FindIndex(args, value => value.Equals(flag, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
        ? args[index + 1] : null;
}

static string NormalizeId(string value)
{
    var chars = value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray();
    var normalized = new string(chars).Trim('_');
    if (normalized.Length == 0) throw new ArgumentException("The clone id is empty after normalization.");
    if (char.IsDigit(normalized[0])) normalized = "Item_" + normalized;
    return normalized.Length > 64 ? normalized[..64] : normalized;
}

static Gbx<CGameItemModel> Parse(byte[] bytes, string path)
{
    try
    {
        return Gbx.Parse<CGameItemModel>(new MemoryStream(bytes, writable: false),
            new GbxReadSettings { SafeSkippableChunks = true });
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Could not parse {path}: {ex.Message}", ex);
    }
}

static Gbx<CGameItemModel> ParseOpaque(byte[] bytes, string path)
{
    try
    {
        return Gbx.Parse<CGameItemModel>(new MemoryStream(bytes, writable: false),
            new GbxReadSettings
            {
                SafeSkippableChunks = true,
                IgnoreExceptionsInBody = true,
                ReadRawBody = true
            });
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Could not parse opaque item {path}: {ex.Message}", ex);
    }
}

static void CreateOpaqueClone(byte[] sourceBytes, string sourcePath, string outputPath, string id, string name)
{
    var source = ParseOpaque(sourceBytes, sourcePath);
    var item = source.Node ?? throw new InvalidOperationException("Source has no CGameItemModel node.");
    var rawBody = source.Body?.RawData.ToArray() ?? throw new InvalidOperationException("Source body has no raw payload.");
    if (rawBody.Length == 0)
        throw new InvalidOperationException("Opaque-preserve mode refuses an empty body.");

    item.Name = name;
    item.Description = name;
    if (item.Ident is not null)
        item.Ident = item.Ident with { Id = id };

    using (var output = File.Create(outputPath)) source.Save(output);

    var cloneBytes = File.ReadAllBytes(outputPath);
    var clone = ParseOpaque(cloneBytes, outputPath);
    var cloneRawBody = clone.Body?.RawData.ToArray() ?? [];
    if (!rawBody.AsSpan().SequenceEqual(cloneRawBody))
        throw new InvalidOperationException("Opaque-preserve mode changed the raw body payload.");

    var manifestPath = Path.ChangeExtension(outputPath, ".fxclone.json");
    var manifest = new
    {
        source = Path.GetFullPath(sourcePath),
        output = Path.GetFullPath(outputPath),
        sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)),
        outputSha256 = Convert.ToHexString(SHA256.HashData(cloneBytes)),
        cloneId = clone.Node?.Ident?.Id,
        cloneName = clone.Node?.Name,
        itemType = clone.Node?.ItemType.ToString(),
        opaqueBodyBytes = cloneRawBody.Length,
        bodySha256 = Convert.ToHexString(SHA256.HashData(cloneRawBody)),
        bodyPreserved = true,
        unknownCarrier = "0x0915C000 CPlugFxSystem (opaque body)",
        nativeRuntimeVerdict = "not-tested"
    };
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
}

static void Verify(string path)
{
    var bytes = File.ReadAllBytes(path);
    var parsed = Parse(bytes, path);
    var item = parsed.Node ?? throw new InvalidOperationException("Clone has no item node.");
    var refsBeforeChecks = parsed.RefTable?.Files.Select(file => $"{file.FilePath};flags={file.Flags};use={file.UseFile};node={file.NodeIndex}") ?? [];
    Console.WriteLine($"INSPECT: {Path.GetFileName(path)} itemType={item.ItemType} entity={item.EntityModel?.GetType().Name ?? "null"} ancestor={parsed.RefTable?.AncestorLevel} refs=[{string.Join(" | ", refsBeforeChecks)}]");
    var variantCount = item.EntityModel is NPlugItem_SVariantList { Variants.Length: > 0 } list
        ? list.Variants!.Length
        : item.EntityModel is CPlugPrefab ? 1 : throw new InvalidOperationException("Clone lost its carrier.");
    if (item.ItemType != CGameItemModel.EItemType.Ornament)
        throw new InvalidOperationException($"Clone item type changed to {item.ItemType}.");
    if (parsed.Body?.Exception is not null)
        throw new InvalidOperationException($"Clone body is incomplete: {parsed.Body.Exception.Message}");
    var refs = parsed.RefTable?.Files.Select(file => file.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? [];
    var hasPrefabCarrier = refs.Any(path => path.EndsWith(".Prefab.Gbx", StringComparison.OrdinalIgnoreCase))
        || item.EntityModel is CPlugPrefab
        || item.EntityModel is NPlugItem_SVariantList { Variants: { Length: > 0 } variants }
            && variants[0].EntityModel is CPlugPrefab;
    if (!hasPrefabCarrier)
        throw new InvalidOperationException("Clone has no external prefab carrier reference.");
    var refSummary = parsed.RefTable is null ? "(none)" : string.Join(" | ", parsed.RefTable.Files.Select(file => $"{file.FilePath};flags={file.Flags};use={file.UseFile};node={file.NodeIndex}"));
    Console.WriteLine($"PASS: {Path.GetFileName(path)} id={item.Ident?.Id} variants={variantCount} refs={refs.Length} ancestor={parsed.RefTable?.AncestorLevel} refs=[{refSummary}] carrier={string.Join(";", refs.Where(path => path.EndsWith(".Prefab.Gbx", StringComparison.OrdinalIgnoreCase)))}");
}

static void Inspect(string path)
{
    var parsed = Parse(File.ReadAllBytes(path), path);
    var item = parsed.Node ?? throw new InvalidOperationException("File has no CGameItemModel node.");
    var refs = parsed.RefTable?.Files.Select(file => new { file.FilePath, file.Flags, file.UseFile, file.NodeIndex }).ToArray() ?? [];
    var prefab = item.EntityModel as CPlugPrefab;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        path = Path.GetFullPath(path),
        itemType = item.ItemType.ToString(),
        entity = item.EntityModel?.GetType().FullName,
        ident = item.Ident?.Id,
        identCollection = item.Ident?.Collection,
        identAuthor = item.Ident?.Author,
        name = item.Name,
        description = item.Description,
        pageName = item.PageName,
        catalogPosition = item.CatalogPosition,
        prefabUrl = prefab?.Url,
        prefabVersion = prefab?.Version,
        prefabEntCount = prefab?.Ents?.Length,
        prefabEntityTypes = prefab?.Ents?.Select(entry => entry.Model?.GetType().FullName ?? "null").ToArray(),
        ancestorLevel = parsed.RefTable?.AncestorLevel,
        references = refs
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static void InspectPrefab(string path)
{
    using var stream = File.OpenRead(path);
    var parsed = Gbx.Parse<CPlugPrefab>(stream, new GbxReadSettings { SafeSkippableChunks = true });
    var prefab = parsed.Node ?? throw new InvalidOperationException("File has no prefab node.");
    var refs = parsed.RefTable?.Files.Select(file => new { file.FilePath, file.Flags, file.UseFile, file.NodeIndex }).ToArray() ?? [];
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        path = Path.GetFullPath(path),
        url = prefab.Url,
        version = prefab.Version,
        entCount = prefab.Ents?.Length ?? 0,
        entityTypes = prefab.Ents?.Select(entry => entry.Model?.GetType().FullName ?? "null").ToArray() ?? [],
        references = refs
    }, new JsonSerializerOptions { WriteIndented = true }));
}

void PrefixPrefabReferencePaths(object root)
{
    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
    void Walk(object? value)
    {
        if (value is null || value is string || !seen.Add(value)) return;
        if (value is GbxRefTableFile file)
        {
            if (!file.FilePath.StartsWith("Media\\", StringComparison.OrdinalIgnoreCase))
                file.FilePath = "Media\\" + file.FilePath;
            return;
        }
        if (value is GbxRefTable || value is Type) return;
        if (value is IEnumerable sequence)
        {
            foreach (var child in sequence) Walk(child);
            return;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("GBX.NET", StringComparison.Ordinal) != true) return;
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            if (property.CanRead && property.GetIndexParameters().Length == 0 && property.Name is not ("Gbx" or "Chunks"))
                Walk(property.GetValue(value));
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            Walk(field.GetValue(value));
    }
    Walk(root);
}

static void InspectHeader(string path)
{
    using var stream = File.OpenRead(path);
    var header = Gbx.ParseHeader(stream);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        path = Path.GetFullPath(path),
        classId = header.Header.ClassId,
        nodeCount = header.Header.NumNodes,
        ancestorLevel = header.RefTable?.AncestorLevel,
        references = header.RefTable?.Files.Select(file => new { file.FilePath, file.Flags, file.UseFile, file.NodeIndex }).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
}
