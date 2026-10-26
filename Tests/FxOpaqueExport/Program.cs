using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text.Json;
using GBX.NET;
using TM_Item_Studio.Models;

if (args.Length < 1)
    throw new ArgumentException("Usage: FxOpaqueExport <embedded-fx-item.gbx> [output-directory] [demo-clone-id] [demo-display-name]");

Gbx.LZO = new GBX.NET.LZO.MiniLZO();
var sourcePath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(Path.GetDirectoryName(sourcePath)!, "fx-opaque-export-test"));
Directory.CreateDirectory(outputDirectory);
var sourceBytes = File.ReadAllBytes(sourcePath);
var source = FxOpaqueExport.Parse(sourceBytes);
if (!FxOpaqueExport.IsOpaqueFxCarrier(source))
    throw new InvalidDataException("Fixture does not contain an opaque CPlugFxSystem carrier.");
var originalIdentity = source.Node.Ident;
var originalName = source.Node.Name;
var originalPoints = FxEmissionPoint.Read(sourceBytes);
var originalItemPoints = FxEmitterTransform.Read(sourceBytes);
var originalBodyIdentity = FxIdentity.Read(sourceBytes);
var sourceHeaderIdTag = ReadHeaderIdTag(sourceBytes);
var nullIdentitySource = sourceHeaderIdTag == uint.MaxValue && originalBodyIdentity.Id is null;
var expectedHeaderId = nullIdentitySource ? null : "TMIS_FxOpaqueProbe.Item.Gbx";
var originalBody = ReadBody(sourceBytes).Body;
// Header-only metadata serialization must retain the complete raw native
// body, independently of the export's own diagnostic status fields.
var headerOnly = FxOpaqueExport.Parse(sourceBytes);
headerOnly.Node.Ident = new Ident(nullIdentitySource ? string.Empty : "MetadataHashNeutralCheck",
    headerOnly.Node.Ident!.Collection, "Auteur_é");
headerOnly.Node.Name = "Header metadata – body must stay exact";
headerOnly.Node.Description = "Header description changed for hash check";
using var headerOnlyStream = new MemoryStream(); headerOnly.Save(headerOnlyStream);
var metadataBody = ReadBody(headerOnlyStream.ToArray()).Body;
var sourceBodyHash = Convert.ToHexString(SHA256.HashData(originalBody));
if (!metadataBody.AsSpan().SequenceEqual(originalBody)
    || Convert.ToHexString(SHA256.HashData(metadataBody)) != sourceBodyHash)
    throw new InvalidDataException("Header metadata serialization changed the native body bytes or hash.");
var noIdentityChange = FxIdentity.Export(sourceBytes, originalBodyIdentity.Id ?? "UnusedForNullBodyId", originalBodyIdentity.Author);
if (!noIdentityChange.ItemBytes.AsSpan().SequenceEqual(sourceBytes) || noIdentityChange.Proof.Patches.Count != 0)
    throw new InvalidDataException("A no-op identity edit changed the original item bytes.");
using var beforeStream = new MemoryStream(); source.Save(beforeStream);

var result = FxOpaqueExport.Export(source, "TMIS_FxOpaqueProbe", "TMIS FX Opaque Probe", "TM_Item_Studio",
    new FxOpaqueExport.EmissionPoint(1.25f, 2.5f, -.75f));
var itemPath = Path.Combine(outputDirectory, result.ItemFileName);
var settingsPath = Path.Combine(outputDirectory, result.SettingsFileName);
File.WriteAllBytes(itemPath, result.ItemBytes);
File.WriteAllBytes(settingsPath, result.SettingsBytes);

var reparsed = FxOpaqueExport.Parse(result.ItemBytes);
if (!FxOpaqueExport.TryReadEmissionPoint(reparsed, out var actual) || actual is null
    || !Close(actual.X, 1.25f) || !Close(actual.Y, 2.5f) || !Close(actual.Z, -.75f))
    throw new InvalidDataException("Native emission point did not survive export/reparse.");
using var settings = JsonDocument.Parse(result.SettingsBytes);
if (result.ItemFileName != "TMIS_FxOpaqueProbe.Item.Gbx" || result.SettingsFileName != "TMIS_FxOpaqueProbe.fxsettings.json"
    || result.ItemRelativePath != result.ItemFileName
    || result.CloneId != "TMIS_FxOpaqueProbe" || result.HeaderId != expectedHeaderId
    || result.BodyId != (originalBodyIdentity.Id is null ? null : result.ItemRelativePath)
    || result.NativeNullIdentityRetained != nullIdentitySource
    || settings.RootElement.GetProperty("schemaVersion").GetInt32() != 4
    || settings.RootElement.GetProperty("cloneId").GetString() != result.CloneId
    || settings.RootElement.GetProperty("itemId").GetString() != result.HeaderId
    || settings.RootElement.GetProperty("headerId").GetString() != result.HeaderId
    || settings.RootElement.GetProperty("bodyId").GetString() != result.BodyId
    || settings.RootElement.GetProperty("itemRelativePath").GetString() != result.ItemFileName
    || settings.RootElement.GetProperty("installAt").GetString() != "Items/" + result.ItemFileName
    || settings.RootElement.GetProperty("nativeNullIdentityRetained").GetBoolean() != nullIdentitySource
    || settings.RootElement.GetProperty("bodyIdUsesFileId").GetBoolean() != (result.BodyId is null)
    || (string.IsNullOrEmpty(reparsed.Node.Ident?.Id) ? null : reparsed.Node.Ident.Id) != expectedHeaderId)
    throw new InvalidDataException("The chosen clone filename and actual native identities were not reported separately.");
if (nullIdentitySource && ReadHeaderIdTag(result.ItemBytes) != uint.MaxValue)
    throw new InvalidDataException("A native null header ID became a lookback definition during export.");
var point = settings.RootElement.GetProperty("emissionPoint");
if (!Close(point.GetProperty("x").GetSingle(), 1.25f) || !Close(point.GetProperty("y").GetSingle(), 2.5f) || !Close(point.GetProperty("z").GetSingle(), -.75f))
    throw new InvalidDataException("Emission point was not recorded in the sidecar.");
if (!settings.RootElement.GetProperty("nativeOffsetWritten").GetBoolean()
    || !settings.RootElement.GetProperty("metadataSaveBodyByteExact").GetBoolean()
    || settings.RootElement.GetProperty("nativeIdentityProof").GetProperty("sourceBodySha256").GetString() != sourceBodyHash
    || settings.RootElement.GetProperty("standaloneRuntimeVerified").GetBoolean())
    throw new InvalidDataException("Native proof/status fields are not truthful.");
var bodyIdentity = FxIdentity.Read(result.ItemBytes);
if (bodyIdentity.Id != (originalBodyIdentity.Id is null ? null : result.ItemFileName)
    || bodyIdentity.Author != "TM_Item_Studio" || reparsed.Node.Ident?.Author != bodyIdentity.Author
    || bodyIdentity.Name != "TMIS FX Opaque Probe" || bodyIdentity.Description != "TMIS FX Opaque Probe"
    || reparsed.Node.Name != bodyIdentity.Name
    || bodyIdentity.LookbackDefinitionCount != originalBodyIdentity.LookbackDefinitionCount
    || !settings.RootElement.GetProperty("metadataIdentityConsistent").GetBoolean()
    || !settings.RootElement.GetProperty("nativeIdentityProof").GetProperty("originalBodyRestorableExactly").GetBoolean()
    || !settings.RootElement.GetProperty("nativeIdentityProof").GetProperty("lookbackDefinitionSlotsPreserved").GetBoolean())
    throw new InvalidDataException("Header/body identity or existing lookback slots diverged during export.");
if (settings.RootElement.GetProperty("nativeEditProof").GetProperty("patches").GetArrayLength() != originalPoints.Emitters.Count
    || !settings.RootElement.GetProperty("nativeEditProof").GetProperty("originalBodyRestorableExactly").GetBoolean())
    throw new InvalidDataException("The export did not prove all emitter splices.");
if (!result.NativeOffsetWritten || !result.BodyPreserved || settings.RootElement.GetProperty("requiredForGame").GetBoolean())
    throw new InvalidDataException("The optional diagnostic file was incorrectly treated as a runtime dependency.");
var writtenPoints = FxEmitterTransform.Read(result.ItemBytes);
for (var i = 1; i < originalPoints.Emitters.Count; i++)
{
    var before = originalItemPoints.Emitters[i];
    var after = writtenPoints.Emitters[i];
    if (!Close(after.X - actual.X, before.X - originalItemPoints.Primary.X)
        || !Close(after.Y - actual.Y, before.Y - originalItemPoints.Primary.Y)
        || !Close(after.Z - actual.Z, before.Z - originalItemPoints.Primary.Z))
        throw new InvalidDataException("Secondary emitter spacing changed during export.");
}
var second = FxOpaqueExport.Export(source, "TMIS_FxOpaqueProbeAgain", "Second export", "TM_Item_Studio", new(2, 3, 4));
var independentSecond = FxOpaqueExport.Export(FxOpaqueExport.Parse(sourceBytes),
    "TMIS_FxOpaqueProbeAgain", "Second export", "TM_Item_Studio", new(2, 3, 4));
var secondPoint = FxEmitterTransform.Read(second.ItemBytes).Primary;
if (!Close(secondPoint.X, 2) || !Close(secondPoint.Y, 3) || !Close(secondPoint.Z, 4)
    || FxIdentity.Read(second.ItemBytes).Id != (originalBodyIdentity.Id is null ? null : "TMIS_FxOpaqueProbeAgain.Item.Gbx")
    || !second.ItemBytes.AsSpan().SequenceEqual(independentSecond.ItemBytes)
    || second.HeaderId != (nullIdentitySource ? null : "TMIS_FxOpaqueProbeAgain.Item.Gbx")
    || second.ItemRelativePath != second.ItemFileName
    || (nullIdentitySource && ReadHeaderIdTag(second.ItemBytes) != uint.MaxValue))
    throw new InvalidDataException("Repeated export did not start from the retained source bytes.");
if (FxIdentity.Read(second.ItemBytes).Name != "Second export" || FxIdentity.Read(second.ItemBytes).Description != "Second export")
    throw new InvalidDataException("Repeated export did not rewrite the native display name and description.");
var unicode = FxOpaqueExport.Export(source, "UnicodeProbe", "Rook & vonken – keuze", "Auteur_é", new(2, 3, 4));
var unicodeHeader = FxOpaqueExport.Parse(unicode.ItemBytes).Node;
var unicodeBody = FxIdentity.Read(unicode.ItemBytes);
if (unicodeHeader.Ident?.Author != "Auteur_é" || unicodeBody.Author != "Auteur_é"
    || unicodeHeader.Name != "Rook & vonken – keuze" || unicodeBody.Name != unicodeHeader.Name
    || unicodeBody.Description != unicodeHeader.Name)
    throw new InvalidDataException("Explicit UTF-8 author, display name or description was not retained exactly.");
try
{
    FxOpaqueExport.Export(source, "ShouldNotExport", "Invalid", "TM_Item_Studio", new(float.NaN, 0, 0));
    throw new Exception("Expected invalid coordinates to fail.");
}
catch (InvalidDataException) { }
using var afterStream = new MemoryStream(); source.Save(afterStream);
if (source.Node.Ident != originalIdentity || source.Node.Name != originalName
    || !beforeStream.ToArray().AsSpan().SequenceEqual(afterStream.ToArray()))
    throw new InvalidDataException("Export mutated the displayed source document.");

// A null decoded entity and a nonempty raw ornament body alone must never be
// sufficient to label an unknown item as a supported FX carrier.
var (bodyOffset, unknownBody) = ReadBody(sourceBytes);
var unsupportedIdentityBody = unknownBody.ToArray();
BinaryPrimitives.WriteUInt32LittleEndian(unsupportedIdentityBody.AsSpan(originalBodyIdentity.AuthorLengthOffset - 4), 0x40000001);
try
{
    FxIdentity.Export(WithBody(sourceBytes, bodyOffset, unsupportedIdentityBody), "UnsupportedIdentity", "NewAuthor");
    throw new Exception("Expected an aliased body author to be rejected.");
}
catch (InvalidDataException) { }
unknownBody.AsSpan(originalPoints.FxGraphOffset, 8).Clear();
using var unknownStream = new MemoryStream();
unknownStream.Write(sourceBytes.AsSpan(0, bodyOffset));
if (sourceBytes[7] == 'C')
{
    var compressed = Gbx.LZO.Compress(unknownBody);
    unknownStream.Write(BitConverter.GetBytes(unknownBody.Length));
    unknownStream.Write(BitConverter.GetBytes(compressed.Length)); unknownStream.Write(compressed);
}
else unknownStream.Write(unknownBody);
if (FxOpaqueExport.IsOpaqueFxCarrier(FxOpaqueExport.Parse(unknownStream.ToArray())))
    throw new InvalidDataException("An arbitrary unknown ornament was misclassified as an FX carrier.");

// Optional exact product-API output for the separate native acceptance run.
// Keep the source author and choose only a new name and a Y=3.5 emission point.
object? nativeDemo = null;
if (args.Length > 2)
{
    var demo = FxOpaqueExport.Export(source, args[2], args.Length > 3 ? args[3] : args[2],
        originalBodyIdentity.Author, new(0, 3.5f, 0));
    var demoPath = Path.Combine(outputDirectory, demo.ItemFileName);
    File.WriteAllBytes(demoPath, demo.ItemBytes);
    File.WriteAllBytes(Path.Combine(outputDirectory, demo.SettingsFileName), demo.SettingsBytes);
    var demoBody = FxIdentity.Read(demo.ItemBytes);
    var demoPoint = FxEmitterTransform.Read(demo.ItemBytes).Primary;
    if (demo.HeaderId != (nullIdentitySource ? null : demo.ItemFileName)
        || demoBody.Id != (originalBodyIdentity.Id is null ? null : demo.ItemFileName)
        || demo.ItemRelativePath != demo.ItemFileName || !Close(demoPoint.X, 0)
        || !Close(demoPoint.Y, 3.5f) || !Close(demoPoint.Z, 0)
        || demoBody.LookbackDefinitionCount != originalBodyIdentity.LookbackDefinitionCount)
        throw new InvalidDataException("The native demo export does not match its installation path and emission point.");
    nativeDemo = new { output = demoPath, sha256 = Convert.ToHexString(SHA256.HashData(demo.ItemBytes)),
        demo.ItemRelativePath, installAt = "Items/" + demo.ItemRelativePath,
        demo.HeaderId, demo.BodyId, author = demoBody.Author, name = demoBody.Name,
        description = demoBody.Description, emissionPoint = demoPoint,
        nativeRuntimeAcceptance = "Pending separate native test; offline export and reparse only" };
}

var report = new
{
    source = sourcePath,
    output = itemPath,
    settings = settingsPath,
    sourceBytes = sourceBytes.Length,
    outputBytes = result.ItemBytes.Length,
    opaqueBodyBytes = result.OpaqueBodyBytes,
    opaqueBodySha256 = result.OpaqueBodySha256,
    bodyPreserved = result.BodyPreserved,
    nativeOffsetWritten = result.NativeOffsetWritten,
    sourceDocumentUnchanged = true,
    headerBodyIdentityConsistent = true,
    headerBodyNameDescriptionConsistent = true,
    originalNullBodyIdRetained = originalBodyIdentity.Id is null,
    nativeNullIdentityRetained = result.NativeNullIdentityRetained,
    cloneId = result.CloneId,
    itemRelativePath = result.ItemRelativePath,
    nativeNonNullIdMatchesDownloadFileName = result.HeaderId is null || result.HeaderId == result.ItemFileName,
    headerId = result.HeaderId,
    bodyId = result.BodyId,
    sourceHeaderIdTag = $"0x{sourceHeaderIdTag:X8}",
    outputHeaderIdTag = $"0x{ReadHeaderIdTag(result.ItemBytes):X8}",
    metadataSerializationBodyByteExact = true,
    metadataSerializationBodyHashUnchanged = sourceBodyHash,
    explicitUtf8AuthorNameDescriptionExact = true,
    repeatedExportMatchesFreshSource = true,
    identityLookbackSlotsPreserved = true,
    unsupportedIdentityAliasRejected = true,
    noOpIdentityByteExact = true,
    secondaryEmitterSpacingPreserved = true,
    unknownOrnamentRejected = true,
    emissionPoint = new { x = 1.25f, y = 2.5f, z = -.75f },
    nativeDemo
};
var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(outputDirectory, "test-report.json"), reportJson);
Console.WriteLine(reportJson);

static bool Close(float a, float b) => Math.Abs(a - b) <= .001f;
static (int Offset, byte[] Body) ReadBody(byte[] bytes)
{
    using var stream = new MemoryStream(bytes); Gbx.ParseHeader(stream);
    var offset = checked((int)stream.Position - (bytes[7] == 'C' ? 8 : 0));
    if (bytes[7] != 'C') return (offset, bytes[offset..]);
    var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset))];
    Gbx.LZO!.Decompress(bytes[(offset + 8)..], body);
    return (offset, body);
}
static uint ReadHeaderIdTag(byte[] bytes)
{
    var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(17));
    if (count is < 1 or > 32) throw new InvalidDataException("Unsupported header chunk count in fixture.");
    var payload = checked(21 + count * 8);
    for (var i = 0; i < count; i++)
    {
        var chunkId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(21 + i * 8));
        var size = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(25 + i * 8)) & 0x7FFFFFFF));
        if (chunkId == 0x2E001003)
        {
            if (size < 8 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(payload)) != 3)
                throw new InvalidDataException("Unsupported collector header identity lookback version.");
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(payload + 4));
        }
        payload = checked(payload + size);
    }
    throw new InvalidDataException("Collector identity header chunk missing in fixture.");
}
static byte[] WithBody(byte[] source, int bodyOffset, byte[] body)
{
    using var stream = new MemoryStream(); stream.Write(source.AsSpan(0, bodyOffset));
    if (source[7] == 'C')
    {
        var compressed = Gbx.LZO!.Compress(body);
        stream.Write(BitConverter.GetBytes(body.Length)); stream.Write(BitConverter.GetBytes(compressed.Length)); stream.Write(compressed);
    }
    else stream.Write(body);
    return stream.ToArray();
}
