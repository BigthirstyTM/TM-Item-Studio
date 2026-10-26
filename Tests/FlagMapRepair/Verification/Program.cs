using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GBX.NET;

if (args.Length != 2) throw new ArgumentException("Usage: dotnet run --project Tests/FlagMapRepair/Verification -- <repository-root> <new-artifact-directory>");
var root = Path.GetFullPath(args[0]); var target = Path.GetFullPath(args[1]);
Check.That(!File.Exists(target) && !Directory.Exists(target), "Verification artifact directory must not exist");
Directory.CreateDirectory(target); Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var artifacts = Path.Combine(root, "artifacts/flag-selfcontained-20261022");
var original = Path.Combine(artifacts, "playable-five-block-map/fixture/TMIS_Embedded_Flags_Race_NativeFirst.Map.Gbx");
var originalBytes = File.ReadAllBytes(original); var originalDocument = new MapArchive(originalBytes);
var nativeItems = Path.Combine(artifacts, "embedded-v6-three-cold1/native-resaved-extraction");
var folder = "Items/TMIS_MatRemap_Shapes_66c295272549484d8580/";
var knownNames = new[] { "TMIS_MatRemap_Swallowtail.Item.Gbx", "TMIS_MatRemap_Long250.Item.Gbx", "TMIS_MatRemap_Round.Item.Gbx" };
var payloads = knownNames.ToDictionary(name => folder + name, name => File.ReadAllBytes(Path.Combine(nativeItems, name)));
const string sentinelName = "Research/unrelated-marker.bin";
payloads.Add(sentinelName, Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray());
var fixtureZip = DecoratedZip(payloads, sentinelName);
var fixture = WriteFixture("controlled-native-export-missing-dependencies.Map.Gbx", fixtureZip);
var sourceHash = Check.Hash(File.ReadAllBytes(fixture)); var fixtureRaw = new RawZip(fixtureZip);
var checks = new List<object>();
var cli = Path.Combine(root, "Tests/FlagMapRepair/bin/Debug/net8.0/FlagMapRepair.dll");
Check.That(File.Exists(cli), "Build FlagMapRepair before running verification");

var repaired = Path.Combine(target, "repaired.Map.Gbx");
var repair = Run(fixture, repaired); Check.That(repair.ExitCode == 0, repair.Error);
using var manifest = JsonDocument.Parse(File.ReadAllText(repaired + ".manifest.json")); var m = manifest.RootElement;
Check.That(m.GetProperty("repairedItemCount").GetInt32() == 3 && m.GetProperty("restoredDependencyCount").GetInt32() == 3, "Did not repair/restore all three");
var repairedDocument = new MapArchive(File.ReadAllBytes(repaired)); var repairedZip = new RawZip(repairedDocument.Map.EmbeddedZipData!);
Check.That(repairedDocument.Header.SequenceEqual(originalDocument.Header), "Original map header changed");
Check.That(repairedDocument.Map.Blocks?.Count == 5 && repairedDocument.Map.AnchoredObjects?.Count == 4, "Road/anchor count changed");
var sentinelBefore = fixtureRaw.Entries.Single(e => e.Name == sentinelName); var sentinelAfter = repairedZip.Entries.Single(e => e.Name == sentinelName);
Check.That(sentinelBefore.Local.SequenceEqual(sentinelAfter.Local), "Sentinel local record/payload/extra changed");
var centralBefore = sentinelBefore.Central.ToArray(); var centralAfter = sentinelAfter.Central.ToArray(); centralBefore.AsSpan(42, 4).Clear(); centralAfter.AsSpan(42, 4).Clear();
Check.That(centralBefore.SequenceEqual(centralAfter), "Sentinel central metadata/comment changed");
Check.That(ZipComment(fixtureZip).SequenceEqual(ZipComment(repairedDocument.Map.EmbeddedZipData!)), "Archive comment changed");
Check.That(sourceHash == Check.Hash(File.ReadAllBytes(fixture)), "Source fixture changed");
checks.Add(new { name = "restore-three-missing-dependencies-and-normalize-three-items", pass = true, repairedItems = 3, restoredDependencies = 3,
    headerByteExact = true, roads = 5, anchors = 4, unrelatedLocalPayloadExtraMetadataCommentExact = true, archiveCommentExact = true,
    inputSha256 = sourceHash, outputSha256 = Check.Hash(File.ReadAllBytes(repaired)), cli = repair });

var second = Path.Combine(target, "repaired-again.Map.Gbx"); var again = Run(repaired, second); Check.That(again.ExitCode == 0, again.Error);
using var againManifest = JsonDocument.Parse(File.ReadAllText(second + ".manifest.json"));
Check.That(againManifest.RootElement.GetProperty("repairedItemCount").GetInt32() == 0 && againManifest.RootElement.GetProperty("restoredDependencyCount").GetInt32() == 0
    && File.ReadAllBytes(repaired).SequenceEqual(File.ReadAllBytes(second)), "Repair is not byte-idempotent");
checks.Add(new { name = "second-repair-is-byte-identical-no-op", pass = true, cli = again });

var noPrimer = Path.Combine(artifacts, "native-resave-repaired/TMIS_v6_Three_NativeResave_Repaired.Map.Gbx");
Refusal("no-native-primer", noPrimer, "genuine native Flag16m");
var changedBody = payloads.ToDictionary(p => p.Key, p => p.Value.ToArray()); changedBody[folder + knownNames[0]][^1] ^= 1;
Refusal("unknown-native-item-body", WriteFixture("tampered-item.Map.Gbx", DecoratedZip(changedBody, sentinelName)), "Unknown native-written body");
var conflict = payloads.ToDictionary(p => p.Key, p => p.Value.ToArray()); conflict.Add(folder + "TMIS_MatRemap_Placeholder.Material.Gbx", [1, 2, 3]);
Refusal("conflicting-existing-dependency", WriteFixture("conflicting-dependency.Map.Gbx", DecoratedZip(conflict, sentinelName)), "Existing dependency is not the exact");
var package = Path.Combine(artifacts, "material-remap-v6-audited"); var wrongPackage = Path.Combine(target, "wrong-package"); Directory.CreateDirectory(wrongPackage);
foreach (var name in new[] { "TMIS_MatRemap_Placeholder.Material.Gbx", "TMIS_MatRemap.GameSkin.Gbx", "TMIS_MatRemap.TerrainModifier.Gbx" })
    File.WriteAllBytes(Path.Combine(wrongPackage, name), File.ReadAllBytes(Path.Combine(package, name)));
var wrongFile = Path.Combine(wrongPackage, "TMIS_MatRemap_Placeholder.Material.Gbx"); var wrongBytes = File.ReadAllBytes(wrongFile); wrongBytes[^1] ^= 1; File.WriteAllBytes(wrongFile, wrongBytes);
Refusal("wrong-dependency-package-hash", fixture, "fails pinned v6 hash", wrongPackage);
var existingHash = Check.Hash(File.ReadAllBytes(repaired)); var overwrite = Run(fixture, repaired);
Check.That(overwrite.ExitCode == 2 && overwrite.Error.Contains("Refusing to overwrite") && Check.Hash(File.ReadAllBytes(repaired)) == existingHash, "Overwrite was not refused unchanged");
checks.Add(new { name = "existing-output-refused-unchanged", pass = true, cli = overwrite });
var blocked = Path.Combine(target, "blocked-by-sidecar.Map.Gbx"); Directory.CreateDirectory(blocked + ".manifest.json"); var blockedRun = Run(fixture, blocked);
Check.That(blockedRun.ExitCode == 2 && blockedRun.Error.Contains("Refusing to overwrite") && !File.Exists(blocked), "Sidecar directory did not block output creation");
checks.Add(new { name = "existing-sidecar-directory-refused-before-output", pass = true, cli = blockedRun });

// These structural guards are independent of the game's parser and do not write a map.
var badLocalCrc = fixtureZip.ToArray(); var first = fixtureRaw.Entries.OrderBy(e => e.LocalOffset).First(); badLocalCrc[first.LocalOffset + 14] ^= 1;
ZipRefusal("local-central-crc-disagreement", badLocalCrc);
var badExtra = fixtureZip.ToArray(); var sentinelExtra = sentinelBefore.LocalOffset + 30 + Encoding.UTF8.GetByteCount(sentinelName); badExtra[sentinelExtra] = 1; badExtra[sentinelExtra + 1] = 0;
ZipRefusal("zip64-extra-field", badExtra);
var colliding = payloads.ToDictionary(p => p.Key, p => p.Value); colliding.Add("research/UNRELATED-marker.bin", [4]);
ZipRefusal("canonical-case-colliding-entry", PlainZip(colliding));

Check.That(File.ReadAllBytes(original).SequenceEqual(originalBytes), "Original map changed during verification");
var report = new { verificationVersion = 1, scope = "Controlled offline binary-preservation fixture; not a native-save, validation, load, gameplay, or animation test.",
    sourceMap = original, sourceSha256 = Check.Hash(originalBytes), controlledFixture = fixture, controlledFixtureSha256 = sourceHash,
    allChecksPassed = true, checks, gameOrStagingOperationsPerformed = false, nativeValidationAcceptanceTested = false };
File.WriteAllText(Path.Combine(target, "verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { allChecksPassed = true, checks = checks.Count, evidence = Path.Combine(target, "verification.json"), repairedMap = repaired }));

string WriteFixture(string name, byte[] zip)
{
    var path = Path.Combine(target, name); var result = originalDocument.ReplaceZip(zip); using var output = new FileStream(path, FileMode.CreateNew); output.Write(result.Output); return path;
}
CliResult Run(string input, string output, string? dependencyPackage = null)
{
    var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = root };
    foreach (var argument in new[] { cli, "--repair", input, output }) info.ArgumentList.Add(argument);
    if (dependencyPackage is not null) { info.ArgumentList.Add("--dependencies"); info.ArgumentList.Add(dependencyPackage); }
    using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    Check.That(process.WaitForExit(60000), "CLI verification timed out"); return new(process.ExitCode, stdout.GetAwaiter().GetResult().Trim(), stderr.GetAwaiter().GetResult().Trim());
}
void Refusal(string name, string input, string expected, string? dependencyPackage = null)
{
    var output = Path.Combine(target, name + "-refused.Map.Gbx"); var before = Check.Hash(File.ReadAllBytes(input)); var result = Run(input, output, dependencyPackage);
    Check.That(result.ExitCode == 2 && result.Error.Contains(expected) && !File.Exists(output) && !File.Exists(output + ".manifest.json") && Check.Hash(File.ReadAllBytes(input)) == before, "Expected safe refusal failed: " + name + " " + result.Error);
    checks.Add(new { name, pass = true, sourceUnchanged = true, outputAbsent = true, cli = result });
}
void ZipRefusal(string name, byte[] bytes)
{
    var refused = false; string? error = null; try { _ = new RawZip(bytes); } catch (InvalidDataException e) { refused = true; error = e.Message; }
    Check.That(refused, "Malformed ZIP was accepted: " + name); checks.Add(new { name, pass = true, error });
}
static byte[] PlainZip(Dictionary<string, byte[]> payloads)
{
    using var memory = new MemoryStream(); using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        foreach (var pair in payloads) { var entry = zip.CreateEntry(pair.Key); entry.LastWriteTime = new DateTimeOffset(2020, 5, 17, 10, 12, 14, TimeSpan.Zero); entry.ExternalAttributes = 0x12340020; using var data = entry.Open(); data.Write(pair.Value); }
    return memory.ToArray();
}
static byte[] DecoratedZip(Dictionary<string, byte[]> payloads, string sentinel)
{
    var archive = new RawZip(PlainZip(payloads)); using var output = new MemoryStream(); var offsets = new Dictionary<string, uint>(); var centrals = new List<byte[]>();
    byte[] extra = [0xFE, 0xCA, 3, 0, 0x11, 0x22, 0x33]; var comment = Encoding.UTF8.GetBytes("unrelated entry metadata must survive");
    foreach (var entry in archive.Entries)
    {
        offsets[entry.Name] = checked((uint)output.Position); var local = entry.Local.ToArray(); var central = entry.Central.ToArray();
        if (entry.Name == sentinel)
        {
            var nameSize = BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(26)); var dataStart = 30 + nameSize;
            local = local[..dataStart].Concat(extra).Concat(local[dataStart..]).ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(28), (ushort)extra.Length);
            central = central.Concat(extra).Concat(comment).ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(30), (ushort)extra.Length); BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(32), (ushort)comment.Length);
        }
        output.Write(local); centrals.Add(central);
    }
    var cdOffset = checked((uint)output.Position);
    for (var i = 0; i < centrals.Count; i++) { BinaryPrimitives.WriteUInt32LittleEndian(centrals[i].AsSpan(42), offsets[archive.Entries[i].Name]); output.Write(centrals[i]); }
    var cdSize = checked((uint)output.Position - cdOffset); var archiveComment = Encoding.UTF8.GetBytes("controlled offline preservation fixture");
    using (var writer = new BinaryWriter(output, Encoding.UTF8, true)) { writer.Write(0x06054B50u); writer.Write(0u); writer.Write((ushort)centrals.Count); writer.Write((ushort)centrals.Count); writer.Write(cdSize); writer.Write(cdOffset); writer.Write((ushort)archiveComment.Length); writer.Write(archiveComment); }
    var bytes = output.ToArray(); _ = new RawZip(bytes); return bytes;
}
static byte[] ZipComment(byte[] zip)
{
    for (var i = zip.Length - 22; i >= Math.Max(0, zip.Length - 65557); i--)
        if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i)) == 0x06054B50u && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(i + 20)) == zip.Length) return zip[(i + 22)..];
    throw new InvalidDataException("ZIP comment missing");
}
internal static class Check
{
    public static void That(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
internal sealed record CliResult(int ExitCode, string Output, string Error);
