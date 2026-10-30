# GBX.NET 2.5 native verification and Mesh Modeler repair

## Result

Version 0.5.2 fixes the reported detached Mesh Modeler preview surface.
The original item uses a deformation layer to bend its authored flat staircase
into a quarter turn. Its clip layers already occupy their final positions.
Previously the preview drew the flat base beside those clips.

The preview now evaluates supported planar, untwisted single-segment bends,
in layer order and only for the groups selected by the layer mask. GroupIndices
translate crystal group slots to authored mask IDs. Original GBX positions and
control frames remain unchanged. Disabled modifiers are ignored. Unsupported
control versions, multiple segments, twist and invalid frames retain an explicit
preview diagnostic; this is not general deformation support.

## Native evidence (30 October 2026)

- Trackmania build `2026-02-02 17:51:19`, Openplanet `1.29.14`, tm-control-mcp `0.4.0`.
- Original item SHA256:
  `ae871b21232f82ff7b7c277e31e75930a1ef909a1cae3402046f0cb9f88a45c7`.
- Actual browser export SHA256, identical before/after the preview repair:
  `36ee95a33c6bde9e7314851fefb99dd03ed464c23b05a326d5625a67fc32e5a9`.
- Original and exported staircase loaded and placed side by side in the native editor.
  Native screenshots show the same continuous curved geometry.
- Typed native collision snapshots contain 1,403 vertices and 2,206 triangles
  for each item. The two vertex arrays and triangle-index arrays are exactly equal.
- All 425 authored base-layer positions evaluated by the new preview lie within
  0.003601 metres of a native collision vertex. The checked-in regression allows
  0.01 metres to account for captured float precision.
- Nine items were placed, saved in `TMIS_GBX250_Native_20261030.Map.Gbx`,
  reopened in the native editor, and read back with their original IDs,
  positions, rotations, scales and variant indices. The map is 159,980 bytes.
  These cover original/round-trip Mesh Modeler, ordinary export, two-instance
  export, variants (variant 0), merged export, motion export, icon export and flag export.

Local raw evidence is under `artifacts/native-gbx250/`; the native map is in the
Trackmania user `Maps` folder. The numeric oracle is committed under
`Tests/ItemScene/Fixtures/` so CI does not depend on the user's installed game.

## Reproducing the native collision oracle

Place `MeshModeler_Original.Item.Gbx` and `MeshModeler_Roundtrip.Item.Gbx` in
`Items/TMIS_GBX250_Native_20261030/`, place them in a disposable editor map,
then create `collision-snapshot.enable` in that directory. Load the repository's
`TMItemStudioSmokeTest` plugin. It reads the runtime static collision mesh via
typed fields and writes two `.collision.json` files; it does not modify models.
Check fresh `[TMIS-SMOKE] COLLISION-SNAPSHOT` log records, compare the arrays,
then unload the plugin and remove the gate file.

## Verification boundaries

Release build succeeds. All 14 central test suites pass, including 29 ItemScene
checks in the initial bend repair (32 after the NICE-informed polygon and UV
regressions; see `nice-mesh-modeler-research-2026-10-30.md`). The research-project build sweep exposed two explicit source lists
missing the new evaluator; both were corrected and rebuilt successfully.
Browser workflow and searchable-material regressions pass, and the reported
item was imported, visually inspected and exported through the actual UI.

Native parsing, placement, visible staircase shape, collision equivalence,
map saving and map reopening are verified. Entering test mode succeeded, but
the attempted car spawn tipped over near the staircase edge. A completed driving
test and motion playback comparison are therefore **not verified**. Placement
alone does not prove animation, every variant, icon appearance or flag physics.

This change does not resolve the 11 old experimental FX/car corpus probes that
still fail parse/save/reparse, nor prove support for every possible Item.Gbx.
External texture availability and unsupported multi-segment/twisted deformations
remain separate limitations. The compatibility diagnostics must remain visible.
