using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TM_Item_Studio.Models;

/// <summary>
/// Bounded identity, name and description edits in initial collector metadata.
/// Existing lookback definitions retain their slots; a null body ID remains
/// null so the engine can assign the actual item FID path.
/// </summary>
public static class FxIdentity
{
    const int MaxStringBytes = 4096;
    static readonly UTF8Encoding Utf8 = new(false, true);

    public sealed record Inspection(string? Id, string Author, uint Collection,
        int LookbackDefinitionCount, int? IdLengthOffset, int AuthorLengthOffset,
        string Name, string Description, int NameLengthOffset, int DescriptionLengthOffset);
    public sealed record StringPatch(string Field, int SourceLengthOffset, string Before, string After);
    public sealed record Evidence(bool HeaderAndRefTablePreserved, bool OriginalBodyRestorableExactly,
        bool LookbackDefinitionSlotsPreserved, bool NullBodyIdRetained,
        string SourceBodySha256, string OutputBodySha256, IReadOnlyList<StringPatch> Patches);
    public sealed record Result(byte[] ItemBytes, Inspection Before, Inspection After, Evidence Proof);

    public static Inspection Read(ReadOnlyMemory<byte> bytes)
        => Inspect(new FxEmissionPoint.Archive(bytes.ToArray()).Body);

    public static Result Export(ReadOnlyMemory<byte> bytes, string id, string author,
        string? name = null, string? description = null)
    {
        ValidateText(id); ValidateText(author);
        if (name is not null) ValidateText(name);
        if (description is not null) ValidateText(description);
        Require(id.Length > 0 && author.Length > 0, "FX export identity and author cannot be empty.");
        var archive = new FxEmissionPoint.Archive(bytes.ToArray());
        var before = Inspect(archive.Body);
        var patches = new List<StringPatch>();
        if (before.IdLengthOffset is int idOffset && before.Id != id)
            patches.Add(new("body identity", idOffset, before.Id!, id));
        if (before.Author != author)
            patches.Add(new("body author", before.AuthorLengthOffset, before.Author, author));
        if (name is not null && before.Name != name)
            patches.Add(new("body name", before.NameLengthOffset, before.Name, name));
        if (description is not null && before.Description != description)
            patches.Add(new("body description", before.DescriptionLengthOffset, before.Description, description));
        var patched = Splice(archive.Body, patches, reverse: false);
        Require(Splice(patched, patches, reverse: true).AsSpan().SequenceEqual(archive.Body),
            "The original FX body is not exactly restorable from its identity edits.");
        var result = patched.AsSpan().SequenceEqual(archive.Body) ? archive.Source : archive.Write(patched);
        var reopened = new FxEmissionPoint.Archive(result);
        var after = Inspect(reopened.Body);
        Require(reopened.Prefix.AsSpan().SequenceEqual(archive.Prefix), "FX identity editing changed the header or reference table.");
        Require(after.Id == (before.Id is null ? null : id) && after.Author == author
            && after.Collection == before.Collection && after.LookbackDefinitionCount == before.LookbackDefinitionCount
            && after.Name == (name ?? before.Name) && after.Description == (description ?? before.Description),
            "The FX body identity did not reparse with its original lookback slots.");
        return new(result, before, after, new(true, true, true, before.Id is null,
            Hash(archive.Body), Hash(patched), patches.AsReadOnly()));
    }

    static Inspection Inspect(byte[] body)
    {
        var position = 0;
        Require(TakeUInt(body, ref position) == 0x2E001009, "Unsupported FX collector metadata order.");
        TakeString(body, ref position);
        Require(TakeUInt(body, ref position) == 0, "FX identity editing does not support an embedded collector icon FID.");
        Require(TakeUInt(body, ref position) == 3, "FX collector lookback stream must use version 3.");
        Require(TakeUInt(body, ref position) == uint.MaxValue, "Unsupported initial FX collector lookback value.");
        Require(TakeUInt(body, ref position) == 0x2E00100B, "FX collector identity chunk is missing or reordered.");
        var idTag = TakeUInt(body, ref position);
        int? idOffset = null;
        string? id = null;
        var definitions = 0;
        if (idTag == 0x40000000)
        {
            idOffset = position; id = TakeString(body, ref position);
            Require(id.Length > 0, "An empty FX body identity definition is unsupported; use the original null Id form.");
            definitions++;
        }
        else Require(idTag == uint.MaxValue, "FX body identity must be null or its own existing lookback definition.");
        var collection = TakeUInt(body, ref position);
        Require(collection < 0x40000000, "FX identity editing requires an existing numeric collection Id.");
        Require(TakeUInt(body, ref position) == 0x40000000, "FX author must have its own existing lookback definition.");
        var authorOffset = position;
        var author = TakeString(body, ref position);
        Require(author.Length > 0, "FX body author cannot be an empty definition.");
        definitions++;
        Require(TakeUInt(body, ref position) == 0x2E00100C, "FX body name chunk does not follow the identity metadata.");
        var nameOffset = position;
        var name = TakeString(body, ref position);
        Require(TakeUInt(body, ref position) == 0x2E00100D, "FX body description chunk does not follow its name.");
        var descriptionOffset = position;
        var description = TakeString(body, ref position);
        return new(id, author, collection, definitions, idOffset, authorOffset,
            name, description, nameOffset, descriptionOffset);
    }

    static byte[] Splice(byte[] body, IReadOnlyList<StringPatch> patches, bool reverse)
    {
        using var output = new MemoryStream();
        var cursor = 0; var delta = 0;
        foreach (var patch in patches)
        {
            var old = Encode(patch.Before); var replacement = Encode(patch.After);
            var offset = checked(patch.SourceLengthOffset + (reverse ? delta : 0));
            var input = reverse ? replacement : old;
            Require(offset >= cursor && offset <= body.Length - input.Length
                && body.AsSpan(offset, input.Length).SequenceEqual(input), "FX identity splice differs from its recorded source.");
            output.Write(body, cursor, offset - cursor); output.Write(reverse ? old : replacement);
            cursor = offset + input.Length; delta = checked(delta + replacement.Length - old.Length);
        }
        output.Write(body, cursor, body.Length - cursor);
        return output.ToArray();
    }

    static byte[] Encode(string text)
    {
        var bytes = ValidateText(text); var result = new byte[checked(bytes.Length + 4)];
        BinaryPrimitives.WriteInt32LittleEndian(result, bytes.Length); bytes.CopyTo(result, 4); return result;
    }
    static byte[] ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Utf8.GetBytes(text);
        Require(bytes.Length <= MaxStringBytes && !text.Any(char.IsControl), "Unsupported FX identity string length or control characters.");
        return bytes;
    }
    static string TakeString(byte[] body, ref int position)
    {
        var length = TakeUInt(body, ref position);
        Require(length <= MaxStringBytes && (long)position + length <= body.Length, "Truncated or oversized FX metadata string.");
        var text = Utf8.GetString(body, position, checked((int)length)); position += checked((int)length); ValidateText(text); return text;
    }
    static uint TakeUInt(byte[] bytes, ref int position)
    {
        Require(position >= 0 && position <= bytes.Length - 4, "Truncated FX metadata integer.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position)); position += 4; return value;
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}





