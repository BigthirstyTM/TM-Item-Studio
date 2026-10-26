# Resolved-FID Fogger visibility controls

These are offline fixtures. Their generated names identify the intended
visibility test; they do not assert that native particles are visible.

`Tests/FxStandalone/resolved_fogger_identity.py` gives the accepted stock-FID
Fogger its own complete catalog path by replacing the existing header/body ID
strings. Their lookback definition slots remain intact. The mesh deformation
then makes a three-metre tapered tower and the native emitter becomes
`float3(0,3.5,0)`. The resulting file is:

`artifacts/fx-clones/fx-safe-fogger-tower-fullpath-20261025/TMIS_CustomFoggerTower_FullPath_20261025.Item.Gbx`

SHA-256: `E633DBDA9FCD10D285FCD8A11D66BEEB42FBFEE64F8296E871B3167F03D742D1`.

The original Fogger material points at `CustomModAddSelfIllum` and the mesh's
LOD-distance array is empty. `Tests/FxPlacementAudit/resolved_fogger_visibility.py`
creates two bounded controls from that tower, under its `visibility` folder:

| File | SHA-256 | Native fog selector |
| --- | --- | --- |
| `TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx` | `82E1540DF19C9E3B226375BA176A1B5A80DEDAA483D2A9E1B5FA851BB660A079` | true |
| `TMIS_CustomFoggerTower_SpriteVisible_20261025.Item.Gbx` | `A97153015E365273EE380EAC72FCAD94C3498B46A9DE862AB1B8E63798B2A195` | false |

Both are 8,908 bytes and use matching header/body IDs of
`TMIS_FX_SequentialProof\<filename>`. They retain the original native author.
Both retain the accepted `Stadium\Media\Texture\FoggerSmoke.Texture.gbx`,
rate 100, three-metre mesh and item-space emitter height 3.5 metres. They have
15 nodes and no external reference-table entries.

The material configuration is copied from the exact stock-FID Sparkler control
whose native rendering was observed by the runtime owner. It uses
`Stadium\Media\Material\PlatformTech`. The Fogger's existing material-name
definition `TM_AddSelfIllum` remains in its original dictionary slot; blindly
copying the donor's null name would remove that definition. Independent NICE
parsing compares every other material field to the donor. The mesh gains the
native LOD array `[1000000]`. The sprite control additionally changes only
`IsFogEmitter` in the FX archive; all other FX bytes are retained.

Each generator proves that reversing its recorded edits restores the complete
original body. The identity generator also reverses both header-size words.
`visibility/typed-mesh-audit.json` records the strict preview parse: 129
vertices, 96 triangles, identity owner transform, bounds Y=0..3, PlatformTech
link and the new LOD. `visibility/metadata-audit.json` independently decodes
the matching header/body identities. Native loading, actual mesh and smoke
visibility, native saving and reopening remain separate runtime gates.

## Inline image upload investigation

The RGBA8 control's native reports show a real `CPlugFileGen` Pixels image:
512×512×1, four components, RGBA8 format 16, one mip, system-memory pixels and
Color usage. The stock DDS chain uses DXT5 format 5, ten mips, top origin and
different channel metadata. Neither valid node binding nor retained pixels
establishes GPU upload or particle visibility.

The previous flag work supplies no successful inline color-image precedent:
it uses an Item plus sibling material, GameSkin and modifier dependencies and
requires a native primer in the tested configurations. It cannot establish a
single-Item inline-image solution for FX.

The current native `CPlugMaterialUserInst` schema has no inline bitmap field.
Its `UserTextures` records contain an enum and a string. The actual inline
bitmap-reference route is `CPlugMaterial.CustomMaterial.Textures` through
`CPlugMaterialCustom`. The existing material-primer control uses that route
and shares the FX's already-defined bitmap node 14.

`Tests/FxStandalone/material_primer_shader_control.py` makes a minimal
reference-table-only variation of that independently audited primer:

`artifacts/fx-clones/fx-material-upload-control-20261025/TMIS_Sparkler_MaterialPrimer_Techno3Root_20261025.Item.Gbx`

SHA-256: `55470956471A69E8993707B8024B504C78824507EFE0B26827D5A3297717D4CC`.

Only the parent-material FID loses its `GameData\` prefix. The complete body,
original native-null IDs, node order, two inline images and sampler reference
remain byte-identical. The resulting path has a native precedent in the
extracted `CustomModAddSelfIllum.Material.Gbx`. That native source uses
ancestor level three, while this isolated candidate deliberately retains
ancestor zero. Its new path therefore still requires a native resolution
test; this fixture does not claim a solved cross-tree dependency or upload.

An optional second control changes only the two existing native emitter
expressions to decimal-only coordinates, retaining the same physical point:
`TMIS_Sparkler_MaterialPrimer_Techno3Decimal_20261025.Item.Gbx`, SHA-256
`0811550DF6BE7AF6D630BC2D7311F278EADE789E3D0514E8A42B7AA77CBEA5B0`.
Its adjacent point manifest proves both exact reverse splices. It isolates the
old inverse-transform residuals in exponent notation from image-upload tests.
The offline point writer's `--fx-local` route now remains independent of mesh
preview parsing: a later material may legally share a bitmap defined inside
the omitted FX archive, so removing that archive for preview cannot read such
a graph without a separate placeholder treatment.

## Smoke observation and mesh follow-up

The runtime owner subsequently loaded the exact `82E154...` volume control in
its one-item valid-start fixture. White smoke is visible in
`artifacts/fx-clones/fx-custom-fogger-volume-visual-map-20261025/custom-fogger-smoke-visible-tower-unconfirmed.png`.
The associated `native-custom-anchor.json` records the actual custom anchor
and its accepted stock bitmap. The tower mesh is not established by that
screenshot. This observation promotes smoke visibility only; it does not
establish mesh visibility, native-save persistence or a second-client result.

A topology audit explains why the first deformation may be hard to see.
Every original vertex coordinate has absolute value approximately 8 or
7.60319. Its 96 triangles describe double-sided borders around six rectangular
windows. They are a wireframe cube, rather than six filled panels. The original
taper makes the upper borders roughly one centimetre wide.

`Tests/FxPlacementAudit/fogger_thick_frame.py` maps those two coordinate levels
to eighteen-centimetre borders. The same 129 vertices and 96 native triangles
make a three-metre tapered window-frame tower, with the same 2.6-metre base
and 0.5-metre top. Only the Float3 positions and isolated catalog IDs change.
The complete FX, point Y=3.5, normals, UVs, indices, material, LOD and collision
configuration remain exactly preserved from the smoke-visible control.

Output: `fx-safe-fogger-tower-fullpath-20261025/TMIS_CustomFoggerTower_ThickFrame_20261025.Item.Gbx`.
SHA-256: `391C764E18CCC5C16F2D25ABFE7ADB95877BB42D3BCD9862706EB27988C0B372`.
`thick-frame-typed-audit.json` records the independent strict preview parse;
the adjacent manifest proves exact reversal. Native frame visibility remains
to be tested.

Two optional material-name controls retain the existing dictionary definition
slot and change only that material field, plus their unique existing catalog
IDs. `material-name-controls/TMIS_FoggerTower_NamePlatformTech_20261025.Item.Gbx`
uses `PlatformTech` (SHA-256 `2A54F0725BC9FD7D511034915C4AFC35526B9E9C965E85DF49D5EB8C08E51CA5`).
`TMIS_FoggerTower_NameUnique_20261025.Item.Gbx` uses `TMIS_FoggerMeshA`
(SHA-256 `D4188DEEC190999E95D6993488DD48B32E4E55256E712519A4015E625603BA32`).
These isolate a possible material-name cache collision with `TM_AddSelfIllum`;
no native cache cause or result is asserted.

The full typed carrier comparison finds identical owner transforms, null
instance parameters, visual flags 168, mesh flags zero, VisCstType one,
ShadedGeom LOD/LOD-mask one and LOD distance one million. The Fogger requests
generated mesh collision while the visible Sparkler has explicit collision;
the prefab's unknown `U01` also differs. No structural corruption is inferred
from these differences. The comparison is retained in
`material-name-controls/sparkler-fogger-full-mesh-comparison.json`.
