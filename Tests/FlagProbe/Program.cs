using System.Collections;
using System.Reflection;
using System.Text;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Managers;
using GBX.NET.Serialization.Chunking;
using TM_Item_Studio.Models;

// Vanilla flag asset dumper.
//
// Dumps the class/chunk tree of any GBX file so the vanilla flag assets can be read directly
// from the game data instead of from guesses. Chunks are printed with their id, version,
// status (parsed / ignored / unknown) and their parsed public fields; opaque payloads are
// reported with size, an int32-length-prefixed string scan and a hex head.
//
// Usage:
//   dotnet run --project Tests/FlagProbe                            dump the default flag asset set
//   dotnet run --project Tests/FlagProbe -- <file> [<file> ...]      dump specific files
//   dotnet run --project Tests/FlagProbe -- --find <folder> <glob>   dump every match under a folder

Gbx.LZO = new GBX.NET.LZO.MiniLZO();

const string GameData = @"C:\Users\PC\OpenplanetNext\Extract\GameData";

var argsList = args.ToList();
if (argsList.FirstOrDefault() == "--research") return FlagResearch.Run(argsList.Skip(1).ToArray());
var tagsIndex = argsList.IndexOf("--tags");
if (tagsIndex >= 0 && tagsIndex + 1 < argsList.Count)
{
    return ScanTags(argsList[tagsIndex + 1]);
}

var placementIndex = argsList.IndexOf("--placement");
if (placementIndex >= 0 && placementIndex + 1 < argsList.Count)
{
    return ScanPlacement(argsList[placementIndex + 1]);
}

var profileIndex = argsList.IndexOf("--profile");
if (profileIndex >= 0 && profileIndex + 1 < argsList.Count)
{
    // Roots run until the next --flag, so an option value is never mistaken for a root.
    var roots = new List<string>();
    for (var i = profileIndex + 1; i < argsList.Count && !argsList[i].StartsWith("--", StringComparison.Ordinal); i++)
        roots.Add(argsList[i]);
    return ScanProfile(string.Join(";", roots));
}

var convertIndex = argsList.IndexOf("--convert");
if (convertIndex >= 0 && convertIndex + 2 < argsList.Count)
{
    return ConvertItem(argsList[convertIndex + 1], argsList[convertIndex + 2],
        argsList.Contains("--keep-placement"), argsList.Contains("--cloth-static"),
        argsList.Contains("--cloth-external"), argsList.Contains("--cloth-nomesh"));
}

var entriesIndex = argsList.IndexOf("--entries");
if (entriesIndex >= 0)
{
    return ScanEntries(argsList.Skip(entriesIndex + 1).Where(x => !x.StartsWith("--")).ToList());
}

var lodStatsIndex = argsList.IndexOf("--lodstats");
if (lodStatsIndex >= 0 && lodStatsIndex + 1 < argsList.Count)
{
    return ScanLodStats(argsList[lodStatsIndex + 1]);
}

var lodIndex = argsList.IndexOf("--lod");
if (lodIndex >= 0)
{
    return ScanLod(argsList.Skip(lodIndex + 1).Where(x => !x.StartsWith("--")).ToList());
}

var describeIndex = argsList.IndexOf("--describe");
if (describeIndex >= 0 && describeIndex + 1 < argsList.Count)
{
    return DescribeItems(argsList.Skip(describeIndex + 1).Where(x => !x.StartsWith("--")).ToList());
}

var findIndex = argsList.IndexOf("--find");
List<string> paths;
if (findIndex >= 0 && findIndex + 2 < argsList.Count)
{
    var folder = argsList[findIndex + 1];
    var pattern = argsList[findIndex + 2];
    paths = Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();
    Console.WriteLine($"# {paths.Count} file(s) under {folder} matching {pattern}");
}
else if (argsList.Count > 0)
{
    paths = argsList;
}
else
{
    paths =
    [
        Path.Combine(GameData, @"Stadium\Items\Flag16m.Item.Gbx"),
        Path.Combine(GameData, @"Stadium\Items\Flag8m.Item.Gbx"),
        Path.Combine(GameData, @"Stadium\Media\Prefab\Items\Flag\Flag16m.Prefab.Gbx"),
        Path.Combine(GameData, @"Stadium\Media\Prefab\Items\Flag\Flag8m.Prefab.Gbx"),
        Path.Combine(GameData, @"Stadium\Media\Dyna\Flag\Flag.DynaObject.Gbx"),
        Path.Combine(GameData, @"Stadium\Media\Dyna\Flag\FlagSmall.DynaObject.Gbx"),
        Path.Combine(GameData, @"Stadium\Media\PlaceParam\Flag.PlaceParam.Gbx"),
        Path.Combine(GameData, @"Stadium\Media\Material\ItemFlag.Material.Gbx"),
        Path.Combine(GameData, @"Stadium\Media\Material\ItemFlagNoAnim.Material.Gbx"),
    ];
}

var exitedNonZero = 0;
foreach (var path in paths)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"MISSING {path}");
        exitedNonZero = 1;
        continue;
    }
    try
    {
        Dump(path);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED {path}: {ex.GetType().Name}: {ex.Message}");
        exitedNonZero = 1;
    }
    Console.WriteLine();
}
return exitedNonZero;

// One row per item with every field that distinguishes the flag item family from the rest:
// variant tags, variant-list version, the 2E001012 words, the GameSkin header chunk, the icon
// representation and the whole placement class. This is the comparison table the flag
// conversion rules are derived from.
int ScanProfile(string roots)
{
    const string Header = "file\tname\tident\ttype\twaypoint\ttags\tvariantListVersion\tvariantCount"
        + "\tmodelRef\tplacementSource\tflags\tpivotSnap\tcubeSize\tflyVStep\tclassVersion\tsizeGroup"
        + "\talwaysUp\talignInterior\talignWorldDir\tworldDir\tpatchLayouts\tskinFids\ticon"
        + "\tgameSkinChunk\tuValues\tchunks\tbytes";
    Console.WriteLine(Header);
    var rows = new List<string> { Header };

    foreach (var root in roots.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        foreach (var item in Directory.EnumerateFiles(root, "*.Item.Gbx", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var node = Gbx.Parse<CGameItemModel>(item, new GbxReadSettings { SafeSkippableChunks = true }).Node;
                if (node is null) continue;
                var variantList = node.EntityModel as NPlugItem_SVariantList;
                var tags = variantList?.Variants is null ? ""
                    : string.Join("|", variantList.Variants.Select(v => v.Tags is null ? ""
                        : string.Join(",", v.Tags.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"))));
                var modelRefs = variantList?.Variants is null ? ""
                    : string.Join("|", variantList.Variants.Select(v => v.EntityModelFile?.FilePath ?? "inline"));
                var placement = node.DefaultPlacement;
                var placementClass = placement?.PlacementClass;
                var u = node.Chunks.Get(0x2E001012);
                var uFields = u?.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.Name.StartsWith("U0") && f.Name.Length == 3)
                    .OrderBy(f => f.Name).ToList() ?? [];
                var uValues = uFields.Select(f => f.GetValue(u)?.ToString() ?? "");
                var row = string.Join('\t',
                [
                    Path.GetFileName(item),
                    node.Name ?? "",
                    node.Ident?.Id ?? "",
                    node.ItemType.ToString(),
                    node.WaypointType.ToString(),
                    tags,
                    (variantList?.Version.ToString() ?? ""),
                    (variantList?.Variants?.Length.ToString() ?? ""),
                    modelRefs,
                    node.DefaultPlacementFile is not null ? node.DefaultPlacementFile.FilePath
                        : node.DefaultPlacement is not null ? "inline" : "none",
                    (placement?.Flags.ToString() ?? ""),
                    (placement?.PivotSnapDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                    (placement?.CubeSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                    (placement?.FlyVStep.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                    (placementClass?.Version.ToString() ?? ""),
                    (placementClass?.SizeGroup ?? ""),
                    (placementClass?.AlwaysUp.ToString() ?? ""),
                    (placementClass?.AlignToInterior.ToString() ?? ""),
                    (placementClass?.AlignToWorldDir.ToString() ?? ""),
                    (placementClass is null ? "" : placementClass.WorldDir.ToString()),
                    (placementClass?.PatchLayouts?.Length.ToString() ?? ""),
                    (node.NadeoSkinFids?.Length.ToString() ?? ""),
                    (node.Icon is null ? "none" : $"{node.Icon.GetLength(0)}x{node.Icon.GetLength(1)}"),
                    (node.Chunks.Get(0x090F4000) is null ? "no" : "yes"),
                    string.Join(",", uValues),
                    node.Chunks.Count().ToString(),
                    new FileInfo(item).Length.ToString(),
                ]);
                rows.Add(row);
                Console.WriteLine(row);
            }
            catch (Exception ex)
            {
                var row = $"{Path.GetFileName(item)}\tUNREADABLE\t{ex.GetType().Name}: {ex.Message}";
                rows.Add(row);
                Console.WriteLine(row);
            }
        }
    }

    var outIndex = argsList.IndexOf("--out");
    if (outIndex >= 0 && outIndex + 1 < argsList.Count)
    {
        File.WriteAllLines(argsList[outIndex + 1], rows);
        Console.WriteLine($"# written: {argsList[outIndex + 1]}");
    }
    return 0;
}

// Prints one row per item with its placement parameters, placement class and variant tags, so
// the flag's placement can be compared against the rest of the corpus instead of in isolation.
int ScanPlacement(string root)
{
    var items = Directory.EnumerateFiles(root, "*.Item.Gbx", SearchOption.AllDirectories)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    Console.WriteLine("file\ttype\twaypoint\tplacementSource\tflags\tpivotSnap\tcubeSize\tclassVersion"
        + "\tsizeGroup\talwaysUp\talignInterior\talignWorldDir\tworldDir\tpatchLayouts\ttags");
    foreach (var item in items)
    {
        try
        {
            var node = Gbx.Parse<CGameItemModel>(item, new GbxReadSettings { SafeSkippableChunks = true }).Node;
            if (node is null) continue;
            var tags = node.EntityModel is NPlugItem_SVariantList list && list.Variants is not null
                ? string.Join("|", list.Variants.Select(v => v.Tags is null ? ""
                    : string.Join(",", v.Tags.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"))))
                : "";
            var source = node.DefaultPlacementFile is not null ? "file:" + node.DefaultPlacementFile.FilePath
                : node.DefaultPlacement is not null ? "inline" : "none";
            var placement = node.DefaultPlacement;
            var placementClass = placement?.PlacementClass;
            Console.WriteLine(string.Join('\t',
            [
                Path.GetRelativePath(root, item).Replace('\\', '/'),
                node.ItemType.ToString(),
                node.WaypointType.ToString(),
                source,
                (placement?.Flags.ToString() ?? ""),
                (placement?.PivotSnapDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                (placement?.CubeSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                (placementClass?.Version.ToString() ?? ""),
                (placementClass?.SizeGroup ?? ""),
                (placementClass?.AlwaysUp.ToString() ?? ""),
                (placementClass?.AlignToInterior.ToString() ?? ""),
                (placementClass?.AlignToWorldDir.ToString() ?? ""),
                (placementClass is null ? "" : placementClass.WorldDir.ToString()),
                (placementClass?.PatchLayouts?.Length.ToString() ?? ""),
                tags,
            ]));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Path.GetFileName(item)}\tUNREADABLE\t{ex.GetType().Name}: {ex.Message}");
        }
    }
    return 0;
}

// Measures an item, converts it to a flag, and writes a new file next to the original (never over
// it). The written file is reparsed and re-measured, so the report describes the bytes on disk.
int ConvertItem(string input, string output, bool keepPlacement, bool clothStatic = false,
    bool clothExternal = false, bool clothNoMesh = false)
{
    var item = Gbx.Parse<CGameItemModel>(input, new GbxReadSettings { SafeSkippableChunks = true }).Node
        ?? throw new InvalidOperationException($"Could not parse {input}.");
    Console.WriteLine($"IN  {input}  ({new FileInfo(input).Length} bytes)");

    var before = ItemFlagConversion.Inspect(item);
    Console.WriteLine($"  before: isFlag={before.IsFlag} deviations={before.Deviations.Count}");
    foreach (var deviation in before.Deviations)
        Console.WriteLine($"    {deviation.Field}: {deviation.Actual} instead of {deviation.Expected}");
    foreach (var note in before.Notes) Console.WriteLine($"    note: {note}");

    // A repaired copy must be discoverable by the inventory, so it gets its own file-derived id.
    ItemExportValidator.EnsureGameReady(item, Path.GetFileNameWithoutExtension(output));
    var report = ItemFlagConversion.Convert(item, normalizePlacement: !keepPlacement);
    foreach (var entry in report.Applied) Console.WriteLine($"    applied: {entry}");
    foreach (var note in report.Notes) Console.WriteLine($"    note: {note}");

    // The cloth arrangements are the open question: Nadeo keeps the flag's cloth in external files
    // while this item carries it inline, so the two variants below reproduce the vanilla shape and
    // are written as separate files for an in-game A/B test.
    var clothEntry = FindClothEntry(item);
    if ((clothStatic || clothExternal || clothNoMesh) && clothEntry is null)
        Console.WriteLine("    warning: no cloth entry (a dyna object with an inline mesh) was found");

    var references = new GbxRefTable { AncestorLevel = 1 };
    if (clothStatic && clothEntry is { } staticCloth)
    {
        var dyna = (CPlugDynaObjectModel)staticCloth.Entry.Model!;
        staticCloth.Entry.Model = new CPlugStaticObjectModel
        {
            Version = 3,
            Mesh = dyna.Mesh,
            Shape = dyna.StaticShape,
            IsMeshCollidable = false,
        };
        staticCloth.Entry.ModelFile = null;
        staticCloth.Entry.Params = null;
        Console.WriteLine("    applied: cloth rebuilt as a plain static object entry, so no dyna body holds it");
    }
    if (clothNoMesh && clothEntry is { } bareCloth)
    {
        var dyna = (CPlugDynaObjectModel)bareCloth.Entry.Model!;
        dyna.Mesh = null;
        dyna.MeshFile = null;
        Console.WriteLine("    applied: cloth entry kept with its dyna parameters but without any mesh");
    }
    if (clothExternal && clothEntry is { } externalCloth)
    {
        var dyna = (CPlugDynaObjectModel)externalCloth.Entry.Model!;
        var mesh = dyna.Mesh ?? throw new InvalidOperationException("The cloth entry has no inline mesh to write out.");
        var baseName = Path.GetFileNameWithoutExtension(output);
        if (baseName.EndsWith(".Item", StringComparison.OrdinalIgnoreCase)) baseName = baseName[..^5];
        var meshName = baseName + ".Mesh.Gbx";
        var meshPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".", meshName);
        using (var meshStream = new MemoryStream())
        {
            new Gbx<CPlugSolid2Model>(mesh).Save(meshStream);
            File.WriteAllBytes(meshPath, meshStream.ToArray());
        }
        dyna.Mesh = null;
        dyna.MeshFile = new GbxRefTableFile(references, 0, true, meshName);
        Console.WriteLine($"    applied: cloth mesh written to {meshName} and referenced instead of embedded");
    }

    using var stream = new MemoryStream();
    if (clothExternal) new ReferenceTableDocument(item, references).Save(stream);
    else new Gbx<CGameItemModel>(item).Save(stream);
    var directory = Path.GetDirectoryName(Path.GetFullPath(output));
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    File.WriteAllBytes(output, stream.ToArray());

    var reopened = Gbx.Parse<CGameItemModel>(new MemoryStream(stream.ToArray()),
        new GbxReadSettings { SafeSkippableChunks = true }).Node
        ?? throw new InvalidOperationException("The written file did not parse again.");
    var after = ItemFlagConversion.Inspect(reopened);
    Console.WriteLine($"OUT {output}  ({stream.Length} bytes)");
    Console.WriteLine($"  after : isFlag={after.IsFlag} deviations={after.Deviations.Count}");
    foreach (var deviation in after.Deviations)
        Console.WriteLine($"    {deviation.Field}: {deviation.Actual} instead of {deviation.Expected}");
    foreach (var note in after.Notes) Console.WriteLine($"    note: {note}");
    return after.IsFlag ? 0 : 1;
}

// Prints every prefab entry with the transform that places it. Two items can hold byte-identical
// meshes and still render differently, because the entry - not the mesh - decides where the piece
// sits and how big it is. This is the comparison that separates a placement mistake from a mesh
// mistake.
int ScanEntries(List<string> files)
{
    foreach (var file in files)
    {
        Console.WriteLine(new string('=', 100));
        Console.WriteLine($"FILE {file}");
        if (!File.Exists(file)) { Console.WriteLine("  MISSING"); continue; }
        var node = Gbx.Parse(new MemoryStream(File.ReadAllBytes(file)),
            new GbxReadSettings { SafeSkippableChunks = true }).Node;
        if (node is null) { Console.WriteLine("  UNREADABLE"); continue; }
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        EntriesWalk(node, "root", seen, 0);
        Console.WriteLine();
    }
    return 0;
}

void EntriesWalk(object node, string path, HashSet<object> seen, int depth)
{
    if (depth > 12 || !seen.Add(node)) return;
    var type = node.GetType();
    if (type.Name == "CPlugPrefab")
    {
        var entries = type.GetProperty("Ents", BindingFlags.Public | BindingFlags.Instance)?.GetValue(node) as Array;
        Console.WriteLine($"  {Indent(depth)}prefab [{path}]: {entries?.Length ?? 0} entry(ies)");
        if (entries is not null)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries.GetValue(i);
                if (entry is null) { Console.WriteLine($"  {Indent(depth + 1)}[{i}] null"); continue; }
                var entryType = entry.GetType();
                var model = entryType.GetProperty("Model")?.GetValue(entry);
                var modelFile = entryType.GetProperty("ModelFile")?.GetValue(entry) as GbxRefTableFile;
                var position = entryType.GetProperty("Position")?.GetValue(entry);
                var rotation = entryType.GetProperty("Rotation")?.GetValue(entry);
                var parameters = entryType.GetProperty("Params")?.GetValue(entry);
                var parametersFile = entryType.GetProperty("ParamsFile")?.GetValue(entry) as GbxRefTableFile;
                Console.WriteLine($"  {Indent(depth + 1)}[{i}] model {model?.GetType().Name ?? "(none)"}"
                    + $", modelFile {(modelFile is null ? "(none)" : modelFile.FilePath)}"
                    + $", params {parameters?.GetType().Name ?? "(none)"}"
                    + $", paramsFile {(parametersFile is null ? "(none)" : parametersFile.FilePath)}");
                Console.WriteLine($"  {Indent(depth + 2)}Position = {Describe(position)}");
                Console.WriteLine($"  {Indent(depth + 2)}Rotation = {Describe(rotation)}");
                foreach (var property in entryType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
                    if (property.Name is "Model" or "ModelFile" or "Params" or "ParamsFile" or "Position" or "Rotation") continue;
                    object? value;
                    try { value = property.GetValue(entry); } catch { continue; }
                    if (value is null || value is string || value.GetType().IsPrimitive || value is Enum
                        || value.GetType().IsValueType || value is GbxRefTableFile)
                        Console.WriteLine($"  {Indent(depth + 2)}{property.Name} = {Describe(value)}");
                }
            }
        }
    }

    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Node" or "Gbx" or "Logger" or "Header" or "RefTable") continue;
        object? value;
        try { value = property.GetValue(node); } catch { continue; }
        EntriesValue(value, $"{path}/{property.Name}", seen, depth + 1);
    }
}

void EntriesValue(object? value, string path, HashSet<object> seen, int depth)
{
    if (value is null || depth > 14) return;
    if (value is CMwNod child) { EntriesWalk(child, path, seen, depth); return; }
    if (value is IEnumerable enumerable and not string)
    {
        var index = 0;
        foreach (var element in enumerable)
        {
            if (element is CMwNod elementNode) EntriesWalk(elementNode, $"{path}[{index}]", seen, depth);
            index++;
        }
    }
}

// Measures, over a corpus, how many distinct LOD levels a solid's shaded-geometry masks address
// and how many LOD distances it stores. An item renderer picks a level from the distance list; a
// solid whose masks address more levels than the list has distances has a level with no distance,
// which is the shape that can make a mesh vanish at range while its neighbours keep drawing.
int ScanLodStats(string root)
{
    var files = new List<string>();
    files.AddRange(Directory.EnumerateFiles(root, "*.Item.Gbx", SearchOption.AllDirectories));
    files.AddRange(Directory.EnumerateFiles(root, "*.Mesh.Gbx", SearchOption.AllDirectories));
    files.AddRange(Directory.EnumerateFiles(root, "*.Prefab.Gbx", SearchOption.AllDirectories));
    files.Sort(StringComparer.OrdinalIgnoreCase);
    Console.WriteLine("file	path	distances	levels	maskBits	geometry	match");
    var mismatched = 0;
    var total = 0;
    foreach (var file in files)
    {
        try
        {
            var node = Gbx.Parse(new MemoryStream(File.ReadAllBytes(file)),
                new GbxReadSettings { SafeSkippableChunks = true }).Node;
            if (node is null) continue;
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var solids = new List<(string Path, CPlugSolid2Model Solid)>();
            FindSolids(node, "root", seen, 0, solids);
            foreach (var (path, solid) in solids)
            {
                var distances = solid.LodMaxDistAtFov90 ?? [];
                var masks = (solid.ShadedGeoms ?? []).Where(m => m is not null).Select(m => m.LodMask).ToArray();
                var levels = masks.Where(m => m > 0).Select(m => System.Numerics.BitOperations.Log2((uint)m) + 1).DefaultIfEmpty(0).Max();
                var geometry = (solid.Visuals ?? []).Length;
                total++;
                var match = levels <= distances.Length ? "yes" : "NO";
                if (match == "NO")
                {
                    mismatched++;
                    Console.WriteLine(string.Join('	', Path.GetFileName(file), path,
                        $"[{string.Join(",", distances.Select(d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}]",
                        levels.ToString(), string.Join(",", masks), geometry.ToString(), match));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Path.GetFileName(file)}	UNREADABLE	{ex.GetType().Name}: {ex.Message}");
        }
    }
    Console.WriteLine($"# solids: {total}, solids whose highest LOD level has no matching distance: {mismatched}");
    return 0;
}

void FindSolids(object node, string path, HashSet<object> seen, int depth, List<(string, CPlugSolid2Model)> solids)
{
    if (depth > 10 || !seen.Add(node)) return;
    var type = node.GetType();
    if (node is CPlugSolid2Model solid)
    {
        solids.Add((path, solid));
        return;
    }
    if (node is not CMwNod) return;
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Node" or "Gbx" or "Logger" or "Header" or "RefTable") continue;
        object? value;
        try { value = property.GetValue(node); } catch { continue; }
        FindSolidsValue(value, $"{path}/{property.Name}", seen, depth + 1, solids);
    }
}

void FindSolidsValue(object? value, string path, HashSet<object> seen, int depth, List<(string, CPlugSolid2Model)> solids)
{
    if (depth > 12 || value is null) return;
    if (value is CPlugSolid2Model solid) { FindSolids(solid, path, seen, depth, solids); return; }
    if (value is CMwNod child) { FindSolids(child, path, seen, depth, solids); return; }
    if (value is IEnumerable enumerable and not string)
    {
        var index = 0;
        foreach (var element in enumerable)
        {
            if (element is null) { index++; continue; }
            if (element.GetType().Namespace?.StartsWith("GBX.NET") == true)
                FindSolidsValue(element, $"{path}[{index}]", seen, depth + 1, solids);
            index++;
        }
        return;
    }
    var type = value.GetType();
    if (type.IsPrimitive || type.IsValueType || type.Namespace?.StartsWith("GBX.NET") != true) return;
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Node" or "Gbx" or "Logger" or "Header") continue;
        object? propertyValue;
        try { propertyValue = property.GetValue(value); } catch { continue; }
        FindSolidsValue(propertyValue, $"{path}.{property.Name}", seen, depth + 1, solids);
    }
}

// Walks every node of a file and reports the LOD data of each solid: the per-LOD maximum
// distance, the shaded-geometry LOD masks and the visual count. The generic dumper drops float
// arrays (they are neither scalar nor nodes), so LOD distances need their own reader. Dyna object
// nodes are reported with all of their parameters, because a cloth's motion is driven from there.
int ScanLod(List<string> files)
{
    foreach (var file in files)
    {
        Console.WriteLine(new string('=', 100));
        Console.WriteLine($"FILE {file}");
        if (!File.Exists(file)) { Console.WriteLine("  MISSING"); continue; }
        var node = Gbx.Parse(new MemoryStream(File.ReadAllBytes(file)),
            new GbxReadSettings { SafeSkippableChunks = true }).Node;
        if (node is null) { Console.WriteLine("  UNREADABLE"); continue; }
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        LodWalk(node, "root", seen, 0);
        Console.WriteLine();
    }
    return 0;
}

void LodWalk(object node, string path, HashSet<object> seen, int depth)
{
    if (depth > 12 || !seen.Add(node)) return;
    var type = node.GetType();
    var name = type.Name;
    var interesting = name.Contains("Solid", StringComparison.Ordinal)
        || name.Contains("Dyna", StringComparison.Ordinal)
        || name.Contains("Visual", StringComparison.Ordinal)
        || type.GetProperty("LodMaxDistAtFov90") is not null;

    if (interesting)
    {
        Console.WriteLine($"  {Indent(depth)}{name}  [{path}]");
        if (type.GetProperty("LodMaxDistAtFov90", BindingFlags.Public | BindingFlags.Instance) is { } lodProperty)
        {
            var value = lodProperty.GetValue(node) as float[];
            Console.WriteLine($"  {Indent(depth + 1)}LodMaxDistAtFov90 = {(value is null ? "(none)" : $"[{string.Join(", ", value.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}]")}  (from {lodProperty.DeclaringType?.FullName})");
            // Which chunk field carries it: the stored arrays on the node's chunks are printed, so
            // the LOD distances can be written back into the same slot instead of invented.
            if (type.GetProperty("Chunks", BindingFlags.Public | BindingFlags.Instance)?.GetValue(node) is IEnumerable chunks)
            {
                foreach (var chunk in chunks)
                {
                    if (chunk is null) continue;
                    var chunkType = chunk.GetType();
                    var id = chunk is IChunk typed ? typed.Id : 0u;
                    foreach (var field in chunkType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (field.IsStatic) continue;
                        object? fieldValue;
                        try { fieldValue = field.GetValue(chunk); } catch { continue; }
                        if (fieldValue is Array array && array.Length > 0)
                        {
                            var text = string.Join(", ", array.Cast<object?>().Take(12).Select(Describe));
                            Console.WriteLine($"  {Indent(depth + 2)}chunk 0x{id:X8}.{field.Name} = {array.GetType().GetElementType()?.Name}[{array.Length}] {text}");
                        }
                    }
                }
            }
        }
        foreach (var propertyName in new[] { "Visuals", "ShadedGeoms", "Skel", "PreLightGenerator", "CustomMaterials", "Materials" })
        {
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetValue(node) is not System.Collections.IEnumerable items) continue;
            var list = items.Cast<object?>().ToList();
            Console.WriteLine($"  {Indent(depth + 1)}{propertyName} = {list.Count}");
            if (propertyName is "CustomMaterials" or "Materials")
            {
                var materialIndex = 0;
                foreach (var material in list)
                {
                    Console.WriteLine($"  {Indent(depth + 2)}[{materialIndex}] {material?.GetType().Name ?? "null"}");
                    DumpMaterial(material, depth + 3, 0);
                    materialIndex++;
                }
            }
            if (propertyName != "ShadedGeoms") continue;
            foreach (var mapping in list)
            {
                var mask = mapping?.GetType().GetProperty("LodMask")?.GetValue(mapping);
                var visual = mapping?.GetType().GetProperty("VisualIndex")?.GetValue(mapping);
                var material = mapping?.GetType().GetProperty("MaterialIndex")?.GetValue(mapping);
                Console.WriteLine($"  {Indent(depth + 2)}visual {visual}, material {material}, LodMask {mask}");
            }
        }
        if (name.Contains("Dyna", StringComparison.Ordinal))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
                if (property.Name is "Chunks" or "Node" or "Gbx" or "Logger" or "Header") continue;
                object? value;
                try { value = property.GetValue(node); } catch { continue; }
                if (value is null || value is string || value.GetType().IsPrimitive || value is Enum
                    || value.GetType().IsValueType)
                    Console.WriteLine($"  {Indent(depth + 1)}{property.Name} = {Describe(value)}");
                else if (value is GbxRefTableFile reference)
                    Console.WriteLine($"  {Indent(depth + 1)}{property.Name} = external \"{reference.FilePath}\"");
            }
        }

        // Per-visual geometry size: an LOD level with no or duplicated geometry is the difference
        // between a cloth that keeps rendering and one the engine drops at distance.
        if (type.GetProperty("Visuals", BindingFlags.Public | BindingFlags.Instance)?.GetValue(node) is IEnumerable visualNodes)
        {
            var visualIndex = 0;
            foreach (var visual in visualNodes)
            {
                if (visual is null) { visualIndex++; continue; }
                var visualType = visual.GetType();
                var firstStream = (visualType.GetProperty("VertexStreams")?.GetValue(visual) as IEnumerable)?
                    .Cast<object?>().FirstOrDefault();
                var positions = firstStream?.GetType().GetProperty("Positions")?.GetValue(firstStream) as Vec3[];
                var indices = visualType.GetProperty("IndexBuffer")?.GetValue(visual) is { } buffer
                    ? buffer.GetType().GetProperty("Indices")?.GetValue(buffer) as int[] : null;
                BoxAligned? bounds = visual is CPlugVisual plugVisual ? plugVisual.BoundingBox : null;
                var boundsText = bounds is not { } box ? ""
                    : $", bounds center=[{Round(box.X)},{Round(box.Y)},{Round(box.Z)}] half=[{Round(box.X2)},{Round(box.Y2)},{Round(box.Z2)}] "
                        + $"(size {Round(2 * box.X2)}x{Round(2 * box.Y2)}x{Round(2 * box.Z2)})";
                Console.WriteLine($"  {Indent(depth + 2)}visual {visualIndex}: {visualType.Name}, "
                    + $"vertices {(positions?.Length.ToString() ?? "?")}, indices {(indices?.Length.ToString() ?? "?")}{boundsText}");
                if (visualType.GetProperty("Chunks", BindingFlags.Public | BindingFlags.Instance)?.GetValue(visual) is IEnumerable visualChunks)
                {
                    foreach (var chunk in visualChunks)
                    {
                        if (chunk is null) continue;
                        var chunkId = chunk is IChunk typedChunk ? typedChunk.Id : 0u;
                        var parts = new List<string>();
                        foreach (var field in chunk.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (field.IsStatic) continue;
                            object? fieldValue;
                            try { fieldValue = field.GetValue(chunk); } catch { continue; }
                            if (fieldValue is null or "") continue;
                            if (fieldValue is Array array) parts.Add($"{field.Name}={array.GetType().GetElementType()?.Name}[{array.Length}]");
                            else if (fieldValue.GetType().IsPrimitive || fieldValue is Enum) parts.Add($"{field.Name}={Describe(fieldValue)}");
                        }
                        if (parts.Count > 0)
                            Console.WriteLine($"  {Indent(depth + 3)}chunk 0x{chunkId:X8}: {string.Join(" ", parts)}");
                    }
                }
                visualIndex++;
            }
        }
    }

    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Node" or "RefTable" or "Gbx" or "Logger" or "Header") continue;
        object? value;
        try { value = property.GetValue(node); } catch { continue; }
        LodExpand(value, $"{property.Name}", seen, depth + 1);
    }
}

// A material record is a thin wrapper: the interesting slots are the name, the user instance
// (which names the shader technique) and any external material file.
void DumpMaterial(object? material, int depth, int level)
{
    if (material is null || level > 3) return;
    var type = material.GetType();
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        object? value;
        try { value = property.GetValue(material); } catch { continue; }
        switch (value)
        {
            case null:
            case "":
                continue;
            case string text:
                Console.WriteLine($"  {Indent(depth)}{property.Name} = \"{text}\"");
                continue;
            case GbxRefTableFile reference:
                Console.WriteLine($"  {Indent(depth)}{property.Name} = external \"{reference.FilePath}\"");
                continue;
        }
        var valueType = value.GetType();
        if (valueType.IsPrimitive || valueType.IsValueType || value is Enum) 
        {
            Console.WriteLine($"  {Indent(depth)}{property.Name} = {Describe(value)}");
            continue;
        }
        if (valueType.Namespace?.StartsWith("GBX.NET") == true)
            DumpMaterial(value, depth + 1, level + 1);
    }
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object? value;
        try { value = field.GetValue(material); } catch { continue; }
        if (value is null or "") continue;
        if (value is string text) { Console.WriteLine($"  {Indent(depth)}{field.Name} = \"{text}\""); continue; }
        if (value is GbxRefTableFile reference)
            Console.WriteLine($"  {Indent(depth)}{field.Name} = external \"{reference.FilePath}\"");
        else if (value.GetType().IsPrimitive || value is Enum)
            Console.WriteLine($"  {Indent(depth)}{field.Name} = {Describe(value)}");
    }
}

void LodExpand(object? value, string name, HashSet<object> seen, int depth)
{
    if (depth > 14 || value is null || value is string || value is Enum) return;
    var type = value.GetType();
    if (type.IsPrimitive || value is Vec3 || value is Quat || type.IsValueType) return;
    if (value is CMwNod child) { LodWalk(child, name, seen, depth); return; }
    if (value is System.Collections.IEnumerable enumerable)
    {
        var index = 0;
        foreach (var element in enumerable)
        {
            if (element is CMwNod elementNode) LodWalk(elementNode, $"{name}[{index}]", seen, depth);
            else LodExpand(element, $"{name}[{index}]", seen, depth + 1);
            index++;
        }
        return;
    }
    // Records such as CPlugPrefab.EntRef are classes rather than nodes, so their own slots are
    // walked too: the prefab entry is where a dyna object or an external model reference lives.
    if (type.Namespace?.StartsWith("GBX.NET") != true) return;
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Node" or "Gbx" or "Logger" or "Header") continue;
        object? propertyValue;
        try { propertyValue = property.GetValue(value); } catch { continue; }
        LodExpand(propertyValue, $"{name}.{property.Name}", seen, depth + 1);
    }
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object? fieldValue;
        try { fieldValue = field.GetValue(value); } catch { continue; }
        LodExpand(fieldValue, $"{name}.{field.Name}", seen, depth + 1);
    }
}

// Prints identity, variant tags, skins and placement of complete item files, resolving the
// external references that the generic dumper only reports as opaque slots.
int DescribeItems(List<string> files)
{
    foreach (var file in files)
    {
        var node = Gbx.Parse<CGameItemModel>(file, new GbxReadSettings { SafeSkippableChunks = true }).Node;
        if (node is null)
        {
            Console.WriteLine($"{file}: unreadable");
            continue;
        }
        Console.WriteLine(new string('=', 100));
        Console.WriteLine($"ITEM {file}  ({new FileInfo(file).Length} bytes)");
        Console.WriteLine($"  ident        : {node.Ident?.Id ?? "(none)"} / collection {node.Ident?.Collection} / author {node.Ident?.Author}");
        Console.WriteLine($"  name        : {node.Name}");
        Console.WriteLine($"  itemType    : {node.ItemType} / {node.ItemTypeE}");
        Console.WriteLine($"  waypoint    : {node.WaypointType}");
        Console.WriteLine($"  archetypeRef: {node.ArchetypeRef}");
        Console.WriteLine($"  pageName    : {node.PageName}");
        Console.WriteLine($"  entityModel : {node.EntityModel?.GetType().Name ?? "(none)"}");
        Console.WriteLine($"  icon        : {(node.Icon is null ? "(none)" : $"{node.Icon.GetLength(0)}x{node.Icon.GetLength(1)}")}");

        if (node.EntityModel is NPlugItem_SVariantList variants && variants.Variants is not null)
        {
            Console.WriteLine($"  variant list: version {variants.Version}, {variants.Variants.Length} variant(s)");
            for (var i = 0; i < variants.Variants.Length; i++)
            {
                var variant = variants.Variants[i];
                var tags = variant.Tags is null
                    ? "(no tags)"
                    : string.Join(", ", variant.Tags.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
                Console.WriteLine($"    [{i}] tags: {tags}");
                Console.WriteLine($"        inline model : {variant.EntityModel?.GetType().Name ?? "(none)"}");
                Console.WriteLine($"        model file   : {FileOf(variant.EntityModelFile) ?? "(none)"}");
                Console.WriteLine($"        hidden in manual cycle: {variant.HiddenInManualCycle}");
            }
        }

        if (node.DefaultPlacement?.PlacementClass is { } placementClass)
        {
            Console.WriteLine($"  placement class: version {placementClass.Version}, SizeGroup {placementClass.SizeGroup}, "
                + $"AlwaysUp {placementClass.AlwaysUp}, AlignToInterior {placementClass.AlignToInterior}, "
                + $"AlignToWorldDir {placementClass.AlignToWorldDir}, WorldDir {placementClass.WorldDir}, "
                + $"patch layouts {placementClass.PatchLayouts?.Length ?? 0}");
            var layouts = placementClass.PatchLayouts;
            if (layouts is { Length: > 0 })
            {
                var layoutType = layouts[0].GetType();
                Console.WriteLine($"    patch layout type {layoutType.FullName}");
                for (var i = 0; i < layouts.Length; i++)
                {
                    var layout = layouts[i];
                    var parts = layoutType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p =>
                    {
                        object? v;
                        try { v = p.GetValue(layout); } catch (Exception ex) { return $"{p.Name}=<{ex.GetType().Name}>"; }
                        if (v is string[] texts) return $"{p.Name}=[{string.Join("|", texts)}]";
                        return $"{p.Name}={Describe(v)}";
                    });
                    Console.WriteLine($"      [{i}] {string.Join(" ", parts)}");
                }
            }
        }
        Console.WriteLine($"  default placement inline: {node.DefaultPlacement?.GetType().Name ?? "(none)"}");
        Console.WriteLine($"  default placement file  : {FileOf(node.DefaultPlacementFile) ?? "(none)"}");
        Console.WriteLine($"  game skin               : {node.GameSkin?.GetType().Name ?? "(none)"}");

        // Every remaining array/collection slot that stores external references is printed with
        // its resolved file path, which is how the Nadeo skin list becomes readable.
        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
            if (property.Name is "Chunks" or "Gbx" or "Node" or "EntityModel") continue;
            object? value;
            try { value = property.GetValue(node); } catch { continue; }
            if (value is null or string) continue;
            if (value is IEnumerable enumerable and not byte[])
            {
                var entries = enumerable.Cast<object?>().ToList();
                if (entries.Count == 0) continue;
                if (argsList.Contains("--members") && entries[0] is { } firstEntry)
                    Console.WriteLine($"    members of {firstEntry.GetType().FullName}: "
                        + string.Join(", ", firstEntry.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Select(x => $"{x.Name}:{x.PropertyType.Name}")));
                var slotFiles = entries.Select(FileOf).ToList();
                if (slotFiles.All(x => x is null)) continue;
                Console.WriteLine($"  {property.Name} ({entries.Count}):");
                for (var i = 0; i < slotFiles.Count; i++)
                    Console.WriteLine($"    [{i}] {slotFiles[i] ?? entries[i]?.GetType().Name ?? "null"}");
                continue;
            }
            var single = FileOf(value);
            if (single is not null) Console.WriteLine($"  {property.Name}: {single}");
        }
    }
    return 0;
}

// GBX.NET models external slots as wrapper objects rather than plain file references, so the
// file is reached by looking for the nested GbxRefTableFile instead of resolving the node.
static string? FileOf(object? value)
{
    if (value is null) return null;
    if (value is GbxRefTableFile direct) return direct.FilePath;
    var type = value.GetType();
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Node" or "Chunks") continue;
        object? nested;
        try { nested = property.GetValue(value); } catch { continue; }
        if (nested is GbxRefTableFile file) return file.FilePath;
        if (nested is string text && text.Length > 0 && property.Name is "FilePath" or "Url") return text;
    }
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object? nested;
        try { nested = field.GetValue(value); } catch { continue; }
        if (nested is GbxRefTableFile file) return file.FilePath;
    }
    return null;
}

// Walks every *.Item.Gbx under a root and reports the variant tags it carries, so the tag
// vocabulary (for example Type=Flag) is measured over the whole corpus instead of one file.
int ScanTags(string root)
{
    var files = Directory.EnumerateFiles(root, "*.Item.Gbx", SearchOption.AllDirectories).ToList();
    var tagValues = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
    var tagFiles = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    var combos = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    var variantLists = 0;
    var failed = 0;

    foreach (var file in files)
    {
        try
        {
            var node = Gbx.Parse<CGameItemModel>(file, new GbxReadSettings { SafeSkippableChunks = true }).Node;
            if (node?.EntityModel is not NPlugItem_SVariantList list || list.Variants is null) continue;
            variantLists++;
            foreach (var variant in list.Variants)
            {
                if (variant.Tags is null) continue;
                var tagText = string.Join(", ", variant.Tags.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => $"{x.Key}={x.Value}"));
                if (!combos.TryGetValue(tagText, out var names)) combos[tagText] = names = new(StringComparer.Ordinal);
                names.Add(Path.GetFileName(file));
                foreach (var (key, value) in variant.Tags)
                {
                    if (!tagValues.TryGetValue(key, out var values)) tagValues[key] = values = new(StringComparer.Ordinal);
                    values[value] = values.TryGetValue(value, out var count) ? count + 1 : 1;
                    var pair = $"{key}={value}";
                    if (!tagFiles.TryGetValue(pair, out var pairNames)) tagFiles[pair] = pairNames = new(StringComparer.Ordinal);
                    pairNames.Add(Path.GetFileName(file));
                }
            }
        }
        catch
        {
            failed++;
        }
    }

    Console.WriteLine($"items scanned: {files.Count}, variant-list items: {variantLists}, unreadable: {failed}");
    Console.WriteLine();
    Console.WriteLine("TAG KEY\tVALUE\tCOUNT\tFILES WITH THIS VALUE");
    var lines = new List<string> { "tag\tvalue\tcount\tfiles" };
    foreach (var (key, values) in tagValues)
    {
        foreach (var (value, count) in values.OrderByDescending(x => x.Value))
        {
            var names = string.Join(" ", tagFiles[$"{key}={value}"]);
            var line = $"{key}\t{value}\t{count}\t{names}";
            lines.Add(line);
            Console.WriteLine(names.Length > 200 ? line[..200] + " ..." : line);
        }
    }
    Console.WriteLine();
    Console.WriteLine("VARIANT TAG SET\tFILES");
    foreach (var (tagText, names) in combos.OrderByDescending(x => x.Value.Count))
        Console.WriteLine($"{tagText}\t{names.Count}\t{string.Join(" ", names.Take(6))}");

    var outIndex = argsList.IndexOf("--out");
    if (outIndex >= 0 && outIndex + 1 < argsList.Count)
    {
        File.WriteAllLines(argsList[outIndex + 1], lines);
        Console.WriteLine();
        Console.WriteLine($"written: {argsList[outIndex + 1]}");
    }
    return 0;
}

void Dump(string path)
{
    var bytes = File.ReadAllBytes(path);
    var gbx = Gbx.Parse(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true });
    Console.WriteLine(new string('=', 100));
    Console.WriteLine($"FILE {path}");
    Console.WriteLine($"     {bytes.Length} bytes, class 0x{gbx.Header.ClassId:X8} "
        + $"{ClassManager.GetName(gbx.Header.ClassId) ?? "?"}, node {gbx.Node?.GetType().Name ?? "(null)"}");
    var header = gbx.Header;
    Console.WriteLine($"     header type {header.GetType().FullName}");
    foreach (var property in header.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Gbx") continue;
        object? value;
        try { value = property.GetValue(header); } catch { continue; }
        Console.WriteLine($"        header.{property.Name} = {Describe(value)}");
    }
    var referenceTable = gbx.RefTable;
    if (referenceTable is not null)
    {
        if (referenceTable.Files.Count > 0)
        {
            Console.WriteLine($"     ref table: {referenceTable.Files.Count} external file(s)");
            foreach (var file in referenceTable.Files) Console.WriteLine($"        {file}");
        }
        if (referenceTable.Resources.Count > 0)
        {
            Console.WriteLine($"     ref table: {referenceTable.Resources.Count} resource(s)");
            foreach (var resource in referenceTable.Resources) Console.WriteLine($"        {resource}");
        }
    }

    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
    var lines = new List<string>();
    if (gbx.Node is not null) Walk(gbx.Node, "root", lines, seen, 0);
    if (header is not null && (object)header is CMwNod headerNode && !ReferenceEquals(headerNode, gbx.Node))
        Walk(headerNode, "header", lines, seen, 0);

    foreach (var line in lines) Console.WriteLine(line);
}

void Walk(object node, string path, List<string> lines, HashSet<object> seen, int depth)
{
    if (depth > 10 || !seen.Add(node)) return;
    var type = node.GetType();
    var classId = ClassManager.GetId(type) ?? 0u;
    lines.Add($"{Indent(depth)}{type.Name}  0x{classId:X8}  {ClassManager.GetName(classId) ?? "-"}  [{path}]");

    if (type.GetProperty("Chunks", BindingFlags.Public | BindingFlags.Instance)?.GetValue(node) is IEnumerable chunks)
    {
        foreach (var chunk in chunks)
            if (chunk is not null)
                DescribeChunk(chunk, $"{Indent(depth + 1)}chunk", lines);
    }

    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Node" or "RefTable" or "Gbx" or "Logger" or "Header") continue;
        object? value;
        try { value = property.GetValue(node); } catch { continue; }
        if (IsScalar(value, property.PropertyType)) lines.Add($"{Indent(depth + 1)}{property.Name} = {Describe(value)}");
        else Expand(value, property.Name, lines, seen, depth + 1);
    }
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object? value;
        try { value = field.GetValue(node); } catch { continue; }
        if (IsScalar(value, field.FieldType)) lines.Add($"{Indent(depth + 1)}{field.Name} = {Describe(value)}");
        else Expand(value, field.Name, lines, seen, depth + 1);
    }
}

// Scalars and small structs are printed in place; only nodes and collections are walked.
static bool IsScalar(object? value, Type declaredType)
{
    if (value is null) return true;
    if (value is string || value is Enum) return true;
    var type = value.GetType();
    if (type.IsPrimitive) return true;
    if (declaredType.IsArray || typeof(IEnumerable).IsAssignableFrom(declaredType)) return false;
    return type.IsValueType;
}

// Prints a value and recurses through collections, external references and GBX.NET records
// (for example CPlugPrefab.EntRef, which is a class rather than a node).
void Expand(object? value, string name, List<string> lines, HashSet<object> seen, int depth)
{
    if (depth > 14) return;
    switch (value)
    {
        case null:
        case string:
        case Enum:
            return;
    }
    if (value is GbxRefTableFile external)
    {
        lines.Add($"{Indent(depth)}{name} = external reference \"{external.FilePath}\"");
        return;
    }
    var type = value.GetType();
    if (type.IsPrimitive || type.IsValueType) return;
    if (value is CMwNod node)
    {
        Walk(node, name, lines, seen, depth);
        return;
    }
    if (value is IEnumerable enumerable)
    {
        var index = 0;
        foreach (var element in enumerable)
        {
            Expand(element, $"{name}[{index}] {ShortDescribe(element)}", lines, seen, depth + 1);
            index++;
        }
        return;
    }
    if (type.Namespace?.StartsWith("GBX.NET") != true) return;

    lines.Add($"{Indent(depth)}{name} = {ShortDescribe(value)}");
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
        if (property.Name is "Chunks" or "Node" or "Gbx" or "Logger" or "Header") continue;
        object? propertyValue;
        try { propertyValue = property.GetValue(value); } catch { continue; }
        Expand(propertyValue, property.Name, lines, seen, depth + 1);
    }
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object? fieldValue;
        try { fieldValue = field.GetValue(value); } catch { continue; }
        Expand(fieldValue, field.Name, lines, seen, depth + 1);
    }
}

// One-line summary for records, so an entry list stays readable without expanding it.
static string ShortDescribe(object? value) => value switch
{
    null => "null",
    string s => $"\"{s}\"",
    bool b => b ? "true" : "false",
    float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    Enum e => e.ToString(),
    GbxRefTableFile file => $"external \"{file.FilePath}\"",
    CMwNod node => node.GetType().Name,
    _ => value.GetType().IsValueType ? Describe(value) : value.GetType().Name,
};

void DescribeChunk(object chunk, string prefix, List<string> lines)
{
    var type = chunk.GetType();
    var id = chunk is IChunk typed ? typed.Id : 0u;
    var version = chunk is IVersionable versionable ? $"-v{versionable.Version}" : "";
    var status = type == typeof(SkippableChunk) ? "unknown body"
        : chunk is ISkippableChunk skippable && skippable.Ignore ? "ignored"
        : (chunk as ISkippableChunk)?.Data is not null ? "raw"
        : "parsed";
    lines.Add($"{prefix} 0x{id:X8}{version}  {type.Name}  [{status}]");

    var body = chunk as ISkippableChunk;
    if (status is "ignored" or "unknown body" or "raw" && body?.Data is { } data)
    {
        lines.Add($"{prefix}   payload {data.Length} bytes: {Convert.ToHexString(data.AsSpan(0, Math.Min(96, data.Length)))}");
        foreach (var text in ScanStrings(data))
            lines.Add($"{prefix}   string: \"{text}\"");
    }
    if (type == typeof(SkippableChunk)) return;

    // DeclaredOnly fields/properties first (the chunk's own payload), then everything the base
    // classes expose, so a placement/desc chunk is readable without knowing its split.
    foreach (var owner in new[] { true, false })
    {
        var scope = BindingFlags.Public | BindingFlags.Instance | (owner ? BindingFlags.DeclaredOnly : 0);
        foreach (var field in type.GetFields(scope))
        {
            if (field.IsStatic) continue;
            object? value;
            try { value = field.GetValue(chunk); } catch { continue; }
            lines.Add($"{prefix}   {field.Name} = {Describe(value)}");
        }
        foreach (var property in type.GetProperties(scope))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
            object? value;
            try { value = property.GetValue(chunk); } catch { continue; }
            lines.Add($"{prefix}   {property.Name} = {Describe(value)}");
        }
    }
}

static IEnumerable<string> ScanStrings(byte[] data)
{
    var scan = Math.Min(data.Length, 262144);
    var index = 0;
    while (index + 5 <= scan)
    {
        var length = BitConverter.ToInt32(data, index);
        if (length is >= 2 and <= 200 && index + 4 + length <= data.Length)
        {
            var printable = true;
            for (var i = 0; i < length; i++)
            {
                var b = data[index + 4 + i];
                if (b is 9 or 10 or 13 || b is >= 32 and < 127) continue;
                printable = false;
                break;
            }
            if (printable)
            {
                var text = Encoding.ASCII.GetString(data, index + 4, length).Trim();
                if (text.Length >= 2) yield return text;
                index += 4 + length;
                continue;
            }
        }
        index++;
    }
}

static string Describe(object? value) => value switch
{
    null => "null",
    string s => $"\"{s}\"",
    bool b => b ? "true" : "false",
    float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    Enum e => e.ToString(),
    byte[] a => $"byte[{a.Length}] {Convert.ToHexString(a.AsSpan(0, Math.Min(32, a.Length)))}",
    Array a => $"{a.GetType().GetElementType()?.Name}[{a.Length}]",
    CMwNod n => $"<{n.GetType().Name}>",
    _ => value.ToString() ?? "?",
};

static string Indent(int depth) => new string(' ', 2 + depth * 2);

static string Round(float value) =>
    value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

// The cloth entry is the prefab entry whose model is a dyna object holding an inline mesh.
static (CPlugPrefab Prefab, CPlugPrefab.EntRef Entry)? FindClothEntry(CGameItemModel item) =>
    FindClothEntryIn(item.EntityModel, 0);

static (CPlugPrefab, CPlugPrefab.EntRef)? FindClothEntryIn(CMwNod? node, int depth)
{
    if (node is null || depth > 10) return null;
    if (node is NPlugItem_SVariantList list && list.Variants is not null)
    {
        foreach (var variant in list.Variants)
        {
            var nested = FindClothEntryIn(variant.EntityModel, depth + 1);
            if (nested is not null) return nested;
        }
    }
    if (node is CPlugPrefab prefab && prefab.Ents is not null)
    {
        foreach (var entry in prefab.Ents)
        {
            if (entry is null) continue;
            if (entry.Model is CPlugDynaObjectModel { Mesh: not null }) return (prefab, entry);
            var nested = FindClothEntryIn(entry.Model, depth + 1);
            if (nested is not null) return nested;
        }
    }
    return null;
}

/// <summary>A document whose save writes the external reference table, as a file with referenced
/// media needs one.</summary>
sealed class ReferenceTableDocument : Gbx<CGameItemModel>
{
    public ReferenceTableDocument(CGameItemModel item, GbxRefTable references) : base(item) => RefTable = references;
}
