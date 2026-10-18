using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Managers;

namespace TM_Item_Studio.Models;

/// <summary>
/// Convert the open item into a flag item and return the file, following the same shape as
/// <see cref="ItemMultiInstanceExport.Export"/>: the document is saved, reparsed and edited as its
/// own snapshot, so the item the user is editing — and every view, variant and preview derived from
/// it — is never touched. The bytes are reparsed and re-measured before they are handed back, so a
/// file that cannot be reopened, or that does not carry the measured flag signature, is refused.
/// </summary>
public static class ItemFlagExport
{
    public const string IdSuffix = "_Flag";
    private const int MaxIdLength = 64;

    public readonly record struct Result(
        byte[] Bytes,
        string FileName,
        string IdentId,
        ItemFlagConversion.FlagReport Report,
        IReadOnlyList<string> Warnings);

    /// <param name="gbx">The open document. It is only read.</param>
    /// <param name="normalizePlacement">
    /// True to also write the placement-parameter values the vanilla flag sets (flags, cube,
    /// snapping, fly steps); false to apply only the flag's placement class.
    /// </param>
    /// <param name="preferredId">Id to use when the item has no id of its own.</param>
    public static Result Export(Gbx<CGameItemModel> gbx, bool normalizePlacement = true, string? preferredId = null)
    {
        ArgumentNullException.ThrowIfNull(gbx);

        using var document = new MemoryStream();
        gbx.Save(document);
        using var snapshot = new MemoryStream(document.ToArray());
        var working = Gbx.Parse<CGameItemModel>(snapshot, new GbxReadSettings { SafeSkippableChunks = true });
        var item = working.Node ?? throw new InvalidOperationException("The item could not be reopened for export.");

        var existingId = item.Ident?.Id;
        var id = FlagId(!string.IsNullOrWhiteSpace(existingId) ? existingId : preferredId ?? item.Name);

        // The conversion refuses rather than guesses, so this may throw; nothing has been written
        // at that point and the caller keeps its document untouched.
        var report = ItemFlagConversion.Convert(item, normalizePlacement);

        // An item without a stored ident has no ident chunk yet, so the new id would not persist.
        ItemMacroblock.EnsureChunk(item, nameof(CGameItemModel.Ident));
        item.Ident = new Ident(
            id,
            item.Ident?.Collection ?? new Id("Stadium2020"),
            string.IsNullOrWhiteSpace(item.Ident?.Author) ? "TM_Item_Studio" : item.Ident!.Author);

        using var stream = new MemoryStream();
        working.Save(stream);
        var bytes = stream.ToArray();
        Verify(bytes, id);

        var warnings = new List<string>(report.Notes);
        if (!string.Equals(existingId, id, StringComparison.Ordinal))
            warnings.Add($"The item keeps its original id; the export is '{id}' so the original stays usable.");

        return new Result(bytes, id + ".Item.Gbx", id, report, warnings);
    }

    /// <summary>Refuse to hand back a file that cannot be reopened or is not measurably a flag.</summary>
    public static void Verify(byte[] bytes, string expectedId)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var parsed = Gbx.Parse<CGameItemModel>(new MemoryStream(bytes), new GbxReadSettings { SafeSkippableChunks = true });
        var node = parsed.Node ?? throw new InvalidOperationException("The exported item has no readable node.");

        if (parsed.Header.ClassId != ClassManager.GetId(typeof(CGameItemModel)))
            throw new InvalidOperationException($"Unexpected item class id 0x{parsed.Header.ClassId:X8}.");
        if (!string.Equals(node.Ident?.Id, expectedId, StringComparison.Ordinal))
            throw new InvalidOperationException($"The export was written with id '{node.Ident?.Id}' instead of '{expectedId}'.");

        var report = ItemFlagConversion.Inspect(node);
        if (!report.IsFlag)
            throw new InvalidOperationException($"The exported item is not a flag: {report.Summary}");
    }

    /// <summary>Keep the original id recognisable but distinct, so both items stay in the inventory.</summary>
    private static string FlagId(string? baseId)
    {
        var source = string.IsNullOrWhiteSpace(baseId) ? "CustomItem" : baseId.Trim();
        if (source.EndsWith(IdSuffix, StringComparison.Ordinal)) return source;
        var maxBase = Math.Max(1, MaxIdLength - IdSuffix.Length);
        return (source.Length > maxBase ? source[..maxBase] : source) + IdSuffix;
    }
}
