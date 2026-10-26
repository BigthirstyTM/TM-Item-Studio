# FX placement and custom mesh audit, 25 October 2026

The original Python Pyramid candidate retained the FogVolumetric placement and
archetype fields, but differed from the native Save-As clone in two concrete
ways. Its header identity changed while its body identity remained empty. Its
synthetic mesh also removed the source shaded-geometry/material mappings:
`shadedGeoms`, `materialCount` and `customMaterials` changed from one to zero.
Those are structural concerns, not an established explanation for the native
placement failure. A previously open native save warning was another independent
placement blocker.

The native Save-As Fogger cannot safely be regenerated as a complete document
by the current NICE parser. Its particle submodel fails at `0x090B5000`, causing
the enclosing entity chunk to be retained as opaque fallback and preventing
typed access to trailing placement/archetype chunks. This parser limit does not
mean those native chunks are absent.

`Tests/FxPlacementAudit/audit.py` therefore changes only two fixed-width native
identity strings and the actual position stream on a byte-for-byte copy of the
native Save-As file. The resulting tower has the same 129 vertices, native
topology, material links, UVs, node references and complete FX payload. It is
3 metres tall, with a 2.6 metre square base and a 0.5 metre top. The source
Fogger and tower both contain `IsFogEmitter=true` and `FogEmissionRate=100.0`,
verified by independently decoding the `0x090C6002` record.

The actual visible Sparkler source is a different graph, with 24 mesh vertices
and two particle texture references. `Tests/FxPlacementAudit/deform_mesh.py`
supports that source without parsing its unsupported complete FX graph. It
changes the unique 288-byte position block into a flared square stand, 3 metres
tall, with a 0.5 metre base and a 2.4 metre top. The identity/reference prefix
and every decoded body byte outside positions remain exact. This helper can be
applied after a texture-resolution experiment without interfering with its FX
splices.

The Editor++ API deliberately uses different collection numbers for models and
macroblock item specifications. In this environment, model `CollectionId=9`
maps to macroblock collection index `26`. `Editor::MakeItemSpec` performs that
conversion. Changing `spec.collection` to `9` would be incorrect.

Newly staged files and native preload are also distinct from native inventory
registration. Editor++ has a dedicated refresh-new-items implementation in
`Components/Inventory/RefreshItems.as`: it backs up the item, opens it in the
native item editor, invokes native Save Item As, then restores the original
bytes. A fresh inventory-cache scan only indexes the native catalog that already
exists. This distinction should be tested before blaming mesh data when an old
native Save-As path places successfully and a new identity does not.

The machine-readable offline comparison and candidate hashes are in
`artifacts/fx-clones/fx-native-mesh-proof-20261025/placement-audit.json`.
Native runtime verdicts are deliberately left pending in these offline
manifests; the root runtime run owns preload, placement, screenshots and
save/reopen evidence.

## Positive-rate Fogger bitmap diagnosis

Independent decoding of `0x090C6002` establishes a missing primary bitmap in
the known user FogVolumetric source. The original rate-100 source stores
`isTextureFid=true` with an empty path. The native Save-As clone stores
`isTextureFid=false` with a null bitmap node. Both retain
`IsFogEmitter=true` and `FogEmissionRate=100.0`. The stock
`Fogger16M.ParticleModel.Gbx` reference table instead names
`Media\\Texture\\FoggerSmoke.Texture.gbx`. The missing binding is a concrete
render dependency difference, though it is not by itself proof of why no plume
has been observed.

The stock particle-model extract has an opaque encoded payload after its
SplashModel record. The current parser cannot reach its GPU settings, so the
offline work does not substitute guessed stock dynamics for known user bytes.

`Tests/FxStandalone/fogger.py` creates a bounded candidate from the native
positive-rate Fogger. It retains the full native graph and changes only the
null primary bitmap to the native FID-string branch pointing at
`GameData/Stadium/Media/Texture/FoggerSmoke.Texture.gbx`, plus same-width native
identity strings. The output is
`artifacts/fx-clones/fx-standalone-20261025/TMIS_FoggerBuiltinSmokeProbe01.Item.Gbx`.
Its 15-node table and zero external reference-table entries stay intact; the
FID string still requires the immutable stock game bitmap at runtime. This is
not a claim of embedded pixel data or independence from game assets.

A separate `--sprites` candidate changes only `IsFogEmitter` to false in
addition to that binding, testing whether ordinary GPU smoke rendering is
available when the volumetric selector is disabled. It retains all spawn,
life, physics, curve and rate fields. The output is
`TMIS_FoggerBuiltinSpriteProbe1.Item.Gbx`. Both candidates reparse correctly,
and undoing their exact edits reconstructs every original native byte.
Native loading, visual smoke and save/reopen remain unverified at generation.

The separate inline-FileGen Sparkler experiment crashed native preload in the
root runtime run. These Fogger probes deliberately do not use that serializer.
The bitmap-embedding route needs its own corrected native proof before it can
be reused for Fogger delivery.

## Inline bitmap context audit

Both the earlier sparse-index native 1x1 bitmap control and a strictly
sequential first-use-node variant terminated Trackmania during native preload.
Sequential indices therefore did not resolve the malformed inline graph.

The subsequent schema audit found a concrete context difference in native
`CPlugBitmap` chunk `0x09011036`, version 1. After its null `U01` NodeRef, the
standalone texture writes lookback-string stream version 3 before the empty
`U02` Id. The Sparkler item has already initialized that shared stream. Copying
the standalone version word into its inline bitmap shifts the remaining
decoder fields. Both native texture `U02` Ids are null, so no lookback dictionary
entries need to be imported or remapped.

`Tests/FxStandalone/reindex.py` records and verifies the exact source NodeRef
positions and classes. It allocates the first bitmap/image as 14/15, shifts the
second emitter graph to 16..20, allocates its bitmap/image as 21/22, then shifts
placement and placement options to 23/24. It removes only the verified bitmap
lookback version word and uses the exact native 1x1 pixel payload. Reversing its
recorded index edits and insertions recovers the original source body exactly.
No enclosing skippable-chunk size spans the FX insertion positions.

The corrected audit candidate is
`artifacts/fx-clones/fx-sequential-bitmap-20261025/TMIS_Sparkler_SeqTinyLookbackFixed.Item.Gbx`,
2681 bytes, SHA-256
`EB40C5576230FA37CE22827FB6FB3E3FB2970CAB98A4F4916BA2A67B6D35704E`.
Its manifest lists the exact index edits, inserted node sizes and removed
lookback offset. The root runtime subsequently reported that native preload
completed for this corrected candidate: both GPU particle models had non-null
inline bitmaps, empty bitmap FID strings, and the game remained alive. This
isolates the extra lookback version as the concrete failure fixed by this
control. It is not a visual Sparkler deliverable because its pixel data is the
tiny audit texture.

`Tests/FxStandalone/create_embedded_fogger.py` applies the corrected helper to
the real FoggerSmoke DDS and its native bitmap wrapper. It preserves the
positive volumetric selector and rate 100, inserts bitmap/image nodes 13/14,
and shifts placement/options to 15/16. The paired `--sprites` control changes
only `IsFogEmitter` to false. Both have zero external references and recover
every original source byte after undoing their recorded edits.

Combined mesh candidates under
`artifacts/fx-clones/fx-sequential-bitmap-20261025/meshes/` use the same embedded
FX graphs with only visual position data changed: a 3m flared Sparkler stand
and a 3m tapered Fogger tower. Their headers, UVs, indices, materials and all
FX bytes match the corresponding pixel-embedded candidates. Native rendering,
placement and saved-map persistence require the separate runtime proof.

## Native identity and Studio export

Native preloading of a Fogger staged under a new filename still reported its
original body identity. Both its header and initial body `0x2E00100B` metadata
had retained `TMIS_FX_NativeCopy\TMIS_FoggerEmbeddedNativeClone.Item.Gbx`.
The body and header authors also matched the original 22-character login Id.

`Tests/FxStandalone/identity.py` prepares the two tower fixtures with matching
header/body identities and matching 14-byte display names while preserving
the original author, lookback definition slots and every other byte. The exact
stage destinations are recorded in their manifests:

- `Items/TMIS_FX_SequentialProof/CustomFogger_VolumeTower1.Item.Gbx`
- `Items/TMIS_FX_SequentialProof/CustomFogger_SpriteTower1.Item.Gbx`

Studio's opaque export now uses `Models/FxIdentity.cs` after its header-only
metadata save. The bounded parser edits the text and length words of existing
nonempty body identity/author definitions, preserving their lookback slots.
It also rewrites the adjacent, ordinary body Name/Description strings so a
custom display name is present in the actual native body. All other unknown
bytes remain intact.
The Sparkler's original null body Id stays null so the engine can assign its
FID path; its existing author definition still changes to the requested author.
Unsupported collector orders, icon FIDs, aliased identity/author definitions
and malformed strings are rejected. Both phases record reversible edits.

`Tests/FxOpaqueExport` passes with both the compressed Sparkler flare and the
uncompressed Fogger tower. It checks author/identity/name consistency, preserved
lookback definition counts, exact no-op bytes, unsupported-alias rejection,
source immutability and repeated exports. It also checks the new item-space
emission-point translation and preserved secondary-emitter spacing. Native
load/placement of these authored exports remains a runtime responsibility.

The diagnostic sidecar is schema 3 with `coordinateSpace="item"`. It records
item-space emitter points separately from native FX-local points and includes
the owner transform, identity edits and offset-edit proofs. It is optional and
does not supply any effect settings to Trackmania at runtime.

## Fogger visibility controls

The runtime run reported anchored Foggers with native mesh vertex counts but
no visible tower. A strict typed preview audit of both the staged item and the
native-saved map's embedded item found the same 129 vertices, 96 triangles and
3m tower bounds. Their static-object owner is at the origin with identity
rotation; the pivot is also at the origin. The shaded geometry maps visual 0
to material 0 with LOD mask 1. No owner offset explains the missing mesh.

Two concrete differences from the visible Sparkler remain: the Fogger has an
empty LOD-distance array, and its material is `TM_AddSelfIllum` linking to
`Stadium\Media\Material_BlockCustom\CustomModAddSelfIllum`, without explicit
texture or color data. The visible Sparkler has a LOD distance of 1,000,000 and
uses `Stadium\Media\Material\PlatformTech`.

`Tests/FxPlacementAudit/visible_fogger.py` produces unique-identity controls in
`visibility-controls/`: VolumeTower2 changes only the material node;
VolumeTower3 changes only the LOD array; VolumeTower4 changes both; SpriteTower2
applies both to the sprite-selector variant. The material node is copied
exactly from the visible Sparkler. Every control restores its original body
exactly after undoing the recorded metadata/material/LOD edits, with FX and
pixel data unchanged. Strict typed reparse verifies the requested settings and
unchanged geometry. Native visibility is still pending for these controls.

The machine-readable comparisons are `source-saved-mesh-audit.json` and
`typed-visibility-audit.json` in that controls directory.

## Native catalog identity controls

The actual browser exports retained the original source author selected in
Studio. An independent NICE schema audit found the same author in each file's
header and body, including the quarantined native-float variants. The separate
C# test exports explicitly requested `TM_Item_Studio`; those files correctly
contain that author in both copies. The native author observation therefore
does not indicate a body-author rewrite failure.

A remaining native placement difference is the Id representation. The
known-placeable original Sparkler has null header and body Ids. Its native-saved
embedded copy has the full Items-relative filename in both. The browser export
has a bare custom header Id while retaining a null body Id. Native placement
did not complete for that export, so offline metadata consistency alone is not
proof of a valid native catalog entry.

The isolated controls in `fx-header-null-control-20261025/original-bitmap/`
retain the original bitmap configuration. The Sparkler changes only its header
Id to the native null encoding, preserving the compressed body bytes exactly.
The Fogger retains the two existing header/body lookback definition slots but
replaces their Id text with zero-length strings. Both keep names, authors,
emission points, mesh and FX pixels. Independent NICE reads verify the changed
metadata. These remain native preload/placement hypotheses; Studio's export
policy is unchanged until a native result establishes the correct contract.

## Map context and FX owner audit

The untouched supplied map is now a positive gameplay rendering baseline.
Its blank editor view cannot be used as a negative particle-rendering verdict.
The offline map comparison in `map-context-audit-20261025.json` finds no
map-owned activation requirement that disappears in the first native save:

- Exactly one item anchor in both files, with identical item reference and
  author, position `(771,8,766)`, rotation zero, pivot zero, scale 1, flags 1,
  animation phase zero and no waypoint, pack, macroblock, snapped or parent
  links.
- Intro, in-game, end-race, ambiance, podium and global MediaTracker clips are
  all null in both files. There are no macroblock instances or offzones.
- The serialized script metadata contains normal map/editor bookkeeping;
  no custom Sparkler activation variable or item trigger is present.
- Both GBX reference tables are null. The embedded ZIP drops from 93,848 to
  1,956 bytes. The latter contains only the rewritten item and omits its two
  texture wrappers and DDS payloads, matching the earlier dependency audit.

`fx-owner-graph-audit-20261025.json` independently compares the original loose
Sparkler, corrected sequential-inline tiny control and first native-saved
embedded item. All have the same two-entry prefab, version 11, same prefab
unknown flags, static mesh owner at identity, and FX owner at approximately
`(0,.69592285,0)` with quaternion `(-.70710677,0,0,.70710677)`. Both owners
have null parameter nodes and empty U01 strings. The preview's null FX model
is the documented temporary parsing splice; the validated original archive
contains the FX graph. All three graphs are version 10 with a Parallel root,
two direct particle emitters, local points `(0,0,0)` and `(0,0,10)`, zero world
offsets, and no extra contexts or graph variables. Only serialized byte
offsets change due to the bitmap insertion or native metadata rewrite.

This excludes an observed owner/anchor/MediaTracker topology loss as the
explanation for inline blankness. It does not prove that every runtime resource
is initialized. A same-session, same-map original-versus-inline gameplay test
before saving is the bounded control for the remaining resource representation
question.

The stronger raw-body proof is
`native-save-fx-normalized-proof-20261025.json`. Its read-only generator
`Tests/FxPlacementAudit/native_save_audit.py` restores all 6,561 original body
bytes exactly from the 6,626-byte native save after undoing only the 65-byte
collector Id insertion, five recorded lookback-reference increments and the
known first-use node-number changes. Every particle model, expression, owner
record and unknown FX byte is therefore retained by that save. The missing
archive resources are the demonstrated save loss.

## Full-path identity and material upload controls

A cold restart made the header-null Sparkler control placeable. The zero-length
Fogger Id definition control crashed during an isolated native preload and is
quarantined; it is not a valid product export policy. The nonempty full-path
Fogger in `fx-fullpath-control-20261025/` changes only the text/lengths of the
actual browser export's existing header/body Id definitions. It retains both
lookback definition slots and all other body bytes, with exact inverse-splice
proof and independent NICE metadata verification. Its manifest gives the
required exact Items-relative stage filename. Native testing is pending.

The clean same-map gameplay control shows the vanilla Sparkler emitting while
RGBA8 and exact-native inline images remain blank. The actual inline RGBA8
anchor has a non-null CPlugBitmap and image pixels. The optional read-only
RenderInfo observation at bitmap offset A8 is null for both blank controls and
the visibly working vanilla Sparkler, in editor and gameplay. It therefore does
not distinguish upload or rendering success and must not be used as proof that
an embedded image failed GPU initialization. Image representation, resource
resolution and runtime initialization remain hypotheses.

`fx-material-upload-control-20261025/TMIS_Sparkler_MaterialPrimer_20261025.Item.Gbx`
is an isolated three-entry prefab control. It appends a normal static mesh at
`(5,0,0)` using the native CustomModAddSelfIllum material, whose SelfIllum sampler
references the same already defined bitmap node 14 as the first FX emitter.
The only external dependency is a built-in GameData shader-material FID; the
image remains inline. The entire original FX archive is byte-identical. The
reference table uses ancestor level zero and a literal `GameData\Techno3\...`
path, with the native parent-material `UseFile=false` selector. This is the
intended built-in game dependency, but switching from a user item to that game
resource is not established merely by preserving the file schema. No successful
native user-item rendering precedent for this exact branch is claimed.
The
original placement/magnet NodeRefs move from 23/24 to 31/32 so the new material
nodes preserve sequential first-use ordering. Removing the appended entity,
restoring the entity count and undoing those two NodeRefs recovers the source
body exactly. Header and body identities remain native null.

The read-only typed topology audit temporarily omits the unsupported FX archive
and nulls the now undefined shared texture reference in its parse copy only.
It verifies the three prefab entries, actual primer geometry, position, native
sampler fields and shader path. The original candidate is never reserialized.
Production preview/emitter helpers intentionally remain limited to two entries
until native material upload and particle rendering have been established.

## Bounded static Fogger crash inspection

The browser-derived Fogger variants repeatedly reported Windows fault RVA
`0x2EDBA8`. Read-only disassembly of the installed 2026.2.2.1751 executable
(SHA-256 `3FC7D8CDA542BEDA131C44306B123F4004D07D7E22F512B46B762AFC29F6EDDA`)
places that exact instruction inside the complete unwind fragment
`0x1402EDB70..0x1402EDC96`:

```asm
1402edb91 test rdx, rdx
1402edb94 je   0x1402edba8
; descriptor parent/class checks in the non-null branch
1402edba8 mov  rax, qword ptr [rdx + 0x30]
```

The surrounding code walks a descriptor-like parent chain, compares a class Id
to `0x01001000`, obtains an allocation size and calls constructor/initializer
callbacks. A validated direct caller reads a DWORD class/type Id, looks up its
descriptor and passes the unchecked result into this function. The lookup
explicitly returns null for Id 0/-1 and otherwise uses the class registry.
This strongly matches a null or invalid descriptor reaching a generic factory;
the immediate site is not a string-copy or memcpy routine.

There is no current crash stack, register state, actual class Id or archive
cursor, so null specifically and the upstream cause are not proven. A shifted
archive/type field remains compatible with the evidence. Expression parsing is
neither established nor excluded as an upstream cause. The changed self-sized
strings cannot be blamed from the fault offset alone.

The complete bounded excerpts, original function bytes, hashes and decision
matrix are in `fx-fogger-crash-static-20261025/static-crash-analysis.json` and
adjacent files. Existing archived Capstone/pefile libraries were used; nothing
was installed and no live process was opened or modified.
