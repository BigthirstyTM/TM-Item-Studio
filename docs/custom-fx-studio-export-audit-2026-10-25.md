# Studio FX export audit, 2026-10-25

This is a read-only source and artifact audit. It does not change Studio's
identity policy, generate a new runtime candidate, or operate Trackmania.
Native control outcomes belong to the root agent.

## Evidence boundary

The supplied original Sparkler map now has observed sparks during gameplay.
An empty editor viewport is therefore insufficient to classify the effect as
broken. The root's full-float inline-image and white-image gameplay controls
were blank. RGBA8 native preload accepted the image format, dimensions and
Color usage, but catalog discovery, placement and gameplay still need their
own result. No source sample should be promoted as working on preload alone.

## Current identity behavior

`FxOpaqueExport.Export` starts from retained uploaded bytes and parses a fresh
document. It writes header identity/name, serializes metadata, and requires the
complete decoded body to remain byte-exact before bounded body edits. Existing
source authors are preserved when the UI author field is unchanged. The
`TM_Item_Studio` author observed in console tests was explicitly supplied by
those tests, not silently substituted in real browser exports.

The current exporter always supplies a bare custom header ID. `FxIdentity`
preserves the body's original ID representation:

| Source shape | Header after current export | Body after current export |
| --- | --- | --- |
| Sparkler, null body ID | chosen custom ID | original null token |
| Fogger, existing ID definition | chosen custom ID | chosen ID in that existing definition |

The final browser artifacts in `final-identity-audit.json` have unique header
IDs and names, matching names in the body, matching authors, and exact matching
download filenames. Sparkler's null body ID remains intentional. This is an
offline consistency result, not proof that Trackmania accepts that header-ID
policy for a clone.

The earlier browser test saved downloads under an unrelated test filename. The
latest test sets both ID and internal Name through the UI, then saves exactly
`download.suggestedFilename()`. Its `exportMapping` records the chosen ID,
suggested and saved filenames and one-file download count.

There is also a UI-copy discrepancy: the Identity panel says an ID is used as
the exported internal name, but the FX branch passes `itemModel.Name` explicitly
and bypasses the ordinary export normalizer. Setting only ID currently retains
the old internal Name. The final tests deliberately set both.

### Catalog lookup is not filesystem lookup

The local E++ source shows:

- `tm-mcp-pack-epp/src/Tools.as`, `PlaceItem`: calls
  `Editor::GetInventoryItemModelByPath(itemPath)`.
- `tm-editor-plus-plus/src/Editor/InventoryCache.as`, `GetItemByPath`: exact
  dictionary lookup; it does not resolve an on-disk filename.
- `CacheInvNode` keys that dictionary with `node.NodeName`.
- `FindInventory.path` returns that exact cached key.

Consequently `item not found` for an actual disk-relative filename can mean a
catalog-key mismatch. Native tests must use `FindInventory.path` verbatim and
record it beside the physical filename and ID. Cold discovery and cache
refresh are separate from native FID parsing.

## Lookback-string slot preservation

`FxIdentity` accepts a narrow collector prefix and requires an existing numeric
collection plus an existing author definition. It never adds or removes an ID
definition. The Sparkler body keeps one definition; Fogger keeps two. Name and
Description are ordinary strings and do not allocate lookback slots.

Each metadata splice changes only an existing UTF-8 string and its length word.
Tags and definition order remain unchanged. Reverse splices restore the entire
original body exactly. All later FX/bitmap bytes remain unchanged, so references
into the enclosing lookback context keep their original indices.

The inline bitmap version-word issue is separate: a standalone bitmap's
lookback version must not be inserted into an already initialized enclosing
context. The corrected saved-map audits confirm that the fixed bitmap records
and original decoded texels survive native map serialization.

If a native control requires an empty Fogger ID value, keep its existing
definition tag and slot. Replacing that definition with a null token would shift
all following lookback definitions. Current `FxIdentity.Read` deliberately
rejects an empty ID definition; support for that form must follow a successful
native control and include explicit slot-preservation tests.

## Numeric expression formatting

The current native writer uses invariant float `R` formatting. It round-trips
the float32 value in the offline reader and cannot introduce a locale comma.
However inverse owner rotation can produce tiny cancellation residuals, for
example:

```text
float3(-4.21432E-14,-6.2263237E-16,2.8040771)
```

The native expression parser's acceptance of this scientific notation has not
been established by an isolated visible gameplay control. Offline acceptance
does not prove native expression compilation. This cannot explain the original
full-float inline-image control being blank when it still used `float3(0)`.

A minimal conditional formatting fix would expand the invariant round-trip
decimal string into plain decimal notation, without changing its numeric value
or rounding tiny components away. It should preserve every original expression
on an unchanged point, keep the existing inverse-splice proof, and test both
very small and boundary values. Do not replace the precise writer with a short
fixed-decimal format: that would lose float32 values and can break no-op or
repeated edits.

The UI uses item-local metres. The validated owner transform maps those to the
native FX frame; Sparkler local Z maps to item Y and includes the owner's
0.69592285 m translation. The reader rejects nonzero/dynamic WorldOffsetExpr.
Dragged coordinates round to millimetres; directly loaded coordinates retain
their original float values. Secondary emitters keep their relative positions.

## Smallest changes after native controls establish policy

1. If the null-header Sparkler control is native-placeable while the bare-ID
   export is not, preserve the proven header-ID representation. Continue to use
   the requested clone stem for the download filename and an explicit unique
   Name. Do not infer the same body-ID policy for Fogger.
2. If an empty existing Fogger ID definition is required, clear only that
   definition's value, retain its slot, and teach the bounded reader that exact
   proven form. Never delete the definition.
3. Make Identity-panel copy match FX behavior, or adopt the proven unique-Name
   normalization in the FX path. A native control must decide the identity
   behavior; this audit does not change it.
4. If a number-only control isolates exponent syntax as a problem, expand `R`
   strings to plain invariant decimals while preserving values and exact no-ops.

## Importing a proven sample

The existing loader can already handle RGBA8 versus RGBA32F opaquely. It does
not need a production image decoder/writer to preserve a working variant.
`LoadFileSet` performs the source-byte retention, bounded FX validation, actual
embedded-mesh preview and variant setup used by normal uploads.

A small sample path can therefore be wired after a working item is available:

- Store the two proven `.Item.Gbx` files as on-demand static assets under
  `wwwroot/samples/fx/`; keep hashes and native evidence in a development
  manifest. Do not add multi-megabyte assets to the initial app assembly.
- A “Load Sparkler example” / “Load Fogger example” button fetches the local
  static bytes and wraps them in a small in-memory `IBrowserFile`, then calls
  the existing `LoadFileSet` entry point. This reuses precisely the uploaded-file
  path and its retained original bytes.
- A download link can serve that same one-file source directly. A manifest or
  optional diagnostic JSON is not a game dependency.
- Verify sample load, real embedded geometry, physical origin edit, one-file
  export and only-file reopen in the browser; repeat native gameplay on the
  actual exported bytes.

Keep unproven research candidates outside the sample menu until their particles
are observed during gameplay. Mesh/material controls on opaque FX carriers
remain explicitly preview-only; source FX/image preservation and native origin
authoring are the supported production path so far.
